using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.DieCutting.Config;
using IIoT.Edge.Module.DieCutting.Config.Parameters;
using IIoT.Edge.Module.DieCutting.Mes;
using IIoT.Edge.Module.DieCutting.Payload;
using IIoT.Edge.Module.Sdk.Base;
using Microsoft.Extensions.Options;

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
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
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
        IMesUploadDiagnosticsStore diagnosticsStore,
        IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters,
        ILogService logger,
        IOptions<DieCuttingModuleOptions> moduleOptions)
        : base(buffer, context, logger)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _codec = codec;
        _context = context;
        _mesChannel = mesChannel;
        _diagnosticsStore = diagnosticsStore;
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

        var freshnessResult = EnsureFreshReadData(freshnessTimeoutMs);
        if (!freshnessResult.IsSuccess)
        {
            await RecordResultAsync(null, freshnessResult).ConfigureAwait(false);
            return;
        }

        var identity = _moduleOptions.MesIdentity.Resolve(_context.DeviceName);
        var windowStartAt = _context.NextWindowStartAt ?? DateTime.Now;
        var snapshot = _codec.CaptureRealtimeSnapshot(identity, windowStartAt);
        var result = await _mesChannel
            .UploadRealtimeAsync(CreateDeviceSession(identity), snapshot, TaskCancellationToken)
            .ConfigureAwait(false);

        await RecordResultAsync(snapshot, result).ConfigureAwait(false);
        _context.NextWindowStartAt = snapshot.WindowCompleteAt;
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
