using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.DieCutting.Config;
using IIoT.Edge.Module.DieCutting.Config.Parameters;
using IIoT.Edge.Module.DieCutting.Mes;
using IIoT.Edge.Module.DieCutting.Payload;
using IIoT.Edge.Module.Sdk.Base;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace IIoT.Edge.Module.DieCutting.Production.Tasks;

/// <summary>
/// 模切实时采样上传任务，定时读取当前 PLC buffer 快照并上传 MES。
/// </summary>
internal sealed class DieCuttingRealtimeSampleUploadTask : PlcTaskBase
{
    private readonly DieCuttingModuleDefinition _definition;
    private readonly DieCuttingSignalCodec _codec;
    private readonly DieCuttingContext _context;
    private readonly IDieCuttingMesScenarioChannel _mesChannel;
    private readonly IDieCuttingProductionRecordStore _productionRecordStore;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> _parameters;
    private readonly DieCuttingModuleOptions _moduleOptions;
    private int _taskLoopInterval;

    /// <summary>
    /// 创建模切实时采样上传任务。
    /// </summary>
    public DieCuttingRealtimeSampleUploadTask(
        DieCuttingModuleDefinition definition,
        IPlcBuffer buffer,
        DieCuttingSignalCodec codec,
        DieCuttingContext context,
        IDieCuttingMesScenarioChannel mesChannel,
        IDieCuttingProductionRecordStore productionRecordStore,
        IMesUploadDiagnosticsStore diagnosticsStore,
        IPlcConnectionManager plcConnectionManager,
        IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters,
        ILogService logger,
        IOptions<DieCuttingModuleOptions> moduleOptions)
        : base(buffer, context, logger)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _codec = codec;
        _context = context;
        _mesChannel = mesChannel;
        _productionRecordStore = productionRecordStore;
        _diagnosticsStore = diagnosticsStore;
        _plcConnectionManager = plcConnectionManager;
        _parameters = parameters;
        _moduleOptions = moduleOptions.Value;
        _taskLoopInterval = NormalizeInterval(_moduleOptions.Runtime.UploadLoopIntervalMs, 10000);
    }

    public override string TaskName => _definition.RealtimeSampleUploadTaskKey;

    protected override int TaskLoopInterval => _taskLoopInterval;

    protected override async Task DoCoreAsync()
    {
        var parameterSnapshot = await _parameters.GetAsync(TaskCancellationToken).ConfigureAwait(false);
        _taskLoopInterval = NormalizeInterval(
            parameterSnapshot.Mes<int>(DieCuttingParams.Mes.上传频率毫秒),
            _moduleOptions.Runtime.UploadLoopIntervalMs);
        var freshnessTimeoutMs = NormalizeInterval(
            parameterSnapshot.Mes<int>(DieCuttingParams.Mes.数据新鲜度超时毫秒),
            _moduleOptions.Runtime.DataFreshnessTimeoutMs);

        if (!parameterSnapshot.Mes<bool>(DieCuttingParams.Mes.启用))
        {
            await RecordResultAsync(null, MesCallResult.Disabled("MES 上传已关闭，模切采样上传暂停。")).ConfigureAwait(false);
            return;
        }

        var connectionResult = EnsurePlcConnected();
        if (!connectionResult.IsSuccess)
        {
            await RecordResultAsync(null, connectionResult).ConfigureAwait(false);
            return;
        }

        var freshnessResult = EnsureFreshReadData(freshnessTimeoutMs);
        if (!freshnessResult.IsSuccess)
        {
            await RecordResultAsync(null, freshnessResult).ConfigureAwait(false);
            return;
        }

        var identity = _moduleOptions.MesIdentity.Resolve(_context.DeviceName);
        var windowStartAt = _context.NextWindowStartAt ?? DateTime.Now;
        var snapshot = _codec.CaptureRealtimeSnapshot(identity, windowStartAt);
        var statusSnapshot = _codec.CaptureDeviceStatusSnapshot();
        var deviceSession = CreateDeviceSession(identity);
        var outboundChanged = HasOutboundChanged(snapshot);
        var statusChanged = HasDeviceStatusChanged(statusSnapshot);
        MesCallResult outboundResult;

        if (outboundChanged)
        {
            await StoreProductionRecordAsync(snapshot).ConfigureAwait(false);
            outboundResult = await _mesChannel
                .UploadRealtimeAsync(deviceSession, snapshot, TaskCancellationToken)
                .ConfigureAwait(false);
            if (outboundResult.IsSuccess)
            {
                _context.LastOutboundFingerprint = snapshot.CreateOutboundFingerprint();
                _context.NextWindowStartAt = snapshot.WindowCompleteAt;
            }
        }
        else
        {
            outboundResult = MesCallResult.Success("模切出站快照未变化，已跳过出站上传。");
        }

        MesCallResult? statusResult = null;
        if (statusChanged)
        {
            if (!statusSnapshot.IsKnownStatus)
            {
                statusResult = MesCallResult.InvalidContext($"模切设备状态码未知，已跳过状态上传，状态码={statusSnapshot.StatusCode}。");
                _context.LastDeviceStatusFingerprint = statusSnapshot.CreateFingerprint();
            }
            else
            {
                statusResult = await _mesChannel
                    .UploadEquipmentStatusAsync(deviceSession, statusSnapshot, TaskCancellationToken)
                    .ConfigureAwait(false);
            }

            if (statusResult.IsSuccess)
            {
                _context.LastDeviceStatusFingerprint = statusSnapshot.CreateFingerprint();
            }

            RecordDeviceStatusResult(statusResult);
        }

        var result = outboundChanged
            ? outboundResult
            : statusResult ?? outboundResult;
        await RecordResultAsync(snapshot, result).ConfigureAwait(false);
    }

    private MesCallResult EnsureFreshReadData(int freshnessTimeoutMs)
    {
        if (Buffer is not IPlcReadSignalFreshness freshness)
        {
            return MesCallResult.InvalidContext("PLC buffer 不支持只读数据新鲜度检查，已跳过模切采样上传。");
        }

        var cutoff = DateTimeOffset.UtcNow.AddMilliseconds(-freshnessTimeoutMs);
        foreach (var signalKey in DieCuttingSignalCodec.RequiredSignalKeys)
        {
            if (!freshness.TryGetReadSignalUpdatedAt(signalKey, out var updatedAt) || updatedAt < cutoff)
            {
                return MesCallResult.InvalidContext($"PLC 只读数据未刷新或已过期，信号={signalKey}。");
            }
        }

        return MesCallResult.Success("PLC 只读数据新鲜。");
    }

    private MesCallResult EnsurePlcConnected()
    {
        var status = _plcConnectionManager.GetRuntimeStatus(_context.NetworkDeviceId);
        if (status?.IsConnected == true)
        {
            return MesCallResult.Success("PLC 已连接。");
        }

        return MesCallResult.InvalidContext("PLC 未连接，模切采样上传暂停。");
    }

    private async Task StoreProductionRecordAsync(DieCuttingRealtimeSnapshot snapshot)
    {
        try
        {
            await _productionRecordStore.AddAsync(
                new DieCuttingProductionRecord
                {
                    ModuleId = _definition.ModuleId,
                    DeviceName = _context.DeviceName,
                    BatchNo = snapshot.PunchingLotNumber,
                    Quantity = snapshot.PunchingQuantity,
                    WindowStartAt = snapshot.WindowStartAt,
                    WindowCompleteAt = snapshot.WindowCompleteAt,
                    PunchingSpeed = snapshot.PunchingSpeed,
                    PlateLengthMm = snapshot.PlateLengthMm,
                    PlateWidthMm = snapshot.PlateWidthMm,
                    ClipNo = snapshot.ClipNo,
                    OperatorCode = snapshot.OperatorCode,
                    MoldCode = snapshot.MoldCode,
                    CutterCode = snapshot.CutterCode,
                    RawFieldsJson = JsonSerializer.Serialize(snapshot.RawItems),
                    CreatedAtUtc = DateTime.UtcNow
                },
                TaskCancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error($"[{_context.DeviceName}] 模切生产数据本地保存失败: {ex.Message}");
        }
    }

    private Task RecordResultAsync(DieCuttingRealtimeSnapshot? snapshot, MesCallResult result)
    {
        if (result.IsSuccess)
        {
            _diagnosticsStore.RecordSuccess(_definition.RealtimeDiagnosticsChannel);
        }
        else
        {
            _diagnosticsStore.RecordFailure(_definition.RealtimeDiagnosticsChannel, result.Message);
        }

        _context.LastRealtimeAt = snapshot?.CapturedAt ?? DateTime.Now;
        _context.LastRealtimeResult = result.Message;
        _context.LastRealtimeSnapshot = snapshot;
        _context.Set($"Runtime.Tasks.{TaskName}.LastUploadOutcome", result.Outcome.ToString());
        _context.Set($"Runtime.Tasks.{TaskName}.LastUploadMessage", result.Message);
        return Task.CompletedTask;
    }

    private bool HasOutboundChanged(DieCuttingRealtimeSnapshot snapshot)
        => !string.Equals(
            _context.LastOutboundFingerprint,
            snapshot.CreateOutboundFingerprint(),
            StringComparison.Ordinal);

    private bool HasDeviceStatusChanged(DieCuttingDeviceStatusSnapshot snapshot)
        => !string.Equals(
            _context.LastDeviceStatusFingerprint,
            snapshot.CreateFingerprint(),
            StringComparison.Ordinal);

    private void RecordDeviceStatusResult(MesCallResult result)
    {
        if (result.IsSuccess)
        {
            _diagnosticsStore.RecordSuccess(_definition.DeviceStatusDiagnosticsChannel);
        }
        else
        {
            _diagnosticsStore.RecordFailure(_definition.DeviceStatusDiagnosticsChannel, result.Message);
        }

        _context.Set($"Runtime.Tasks.{TaskName}.LastDeviceStatusOutcome", result.Outcome.ToString());
        _context.Set($"Runtime.Tasks.{TaskName}.LastDeviceStatusMessage", result.Message);
    }

    private DeviceSession CreateDeviceSession(DieCuttingDeviceIdentity identity)
        => new()
        {
            DeviceId = Guid.Empty,
            ProcessId = Guid.Empty,
            DeviceName = string.IsNullOrWhiteSpace(identity.DeviceName) ? _context.DeviceName : identity.DeviceName,
            ClientCode = string.IsNullOrWhiteSpace(identity.UpperComputerNo)
                ? string.IsNullOrWhiteSpace(identity.DeviceCode) ? _context.DeviceName : identity.DeviceCode
                : identity.UpperComputerNo
        };

    private static int NormalizeInterval(int value, int fallback)
    {
        var normalizedFallback = fallback <= 0 ? 1000 : fallback;
        return Math.Max(500, value <= 0 ? normalizedFallback : value);
    }
}
