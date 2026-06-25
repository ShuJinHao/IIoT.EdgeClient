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
    private readonly DieCuttingProductionPlanService _productionPlanService;
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
        DieCuttingProductionPlanService productionPlanService,
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
        _productionPlanService = productionPlanService;
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

        var planState = await _productionPlanService.GetStateAsync(TaskCancellationToken).ConfigureAwait(false);
        _context.SelectedProductionPlan = planState.CurrentPlan;
        _context.TraceBatchNumber = planState.TraceBatchNumber;
        _context.TraceBatchGeneratedAt = planState.TraceBatchGeneratedAt;
        _context.TraceBatchError = planState.TraceBatchError;

        if (!planState.IsMesEnabled)
        {
            await RecordResultAsync(null, MesCallResult.Disabled("MES 上传已关闭，模切采样上传暂停。")).ConfigureAwait(false);
            return;
        }

        if (planState.IsMesEnabled && planState.RequiresSelection && !planState.HasTraceBatchNumber)
        {
            var message = planState.HasSelectedPlan
                ? "MES 已启用，但当前主批计划尚未生成追溯批次号。"
                : "MES 已启用，请先选择主批计划并生成追溯批次号。";
            await RecordResultAsync(null, MesCallResult.BusinessRejected(message)).ConfigureAwait(false);
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
        var snapshot = _codec.CaptureRealtimeSnapshot(identity, windowStartAt, planState.TraceBatchNumber);
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

    private MesCallResult EnsurePlcConnected()
    {
        var status = _plcConnectionManager.GetRuntimeStatus(_context.NetworkDeviceId);
        if (status?.IsConnected == true)
        {
            return MesCallResult.Success("PLC 已连接。");
        }

        return MesCallResult.InvalidContext("PLC 未连接，模切采样上传暂停。");
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
