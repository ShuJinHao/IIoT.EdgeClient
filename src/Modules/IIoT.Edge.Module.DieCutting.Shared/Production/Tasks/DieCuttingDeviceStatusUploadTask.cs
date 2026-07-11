using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Module.DieCutting.Config;
using IIoT.Edge.Module.DieCutting.Config.Parameters;
using IIoT.Edge.Module.DieCutting.Payload;
using IIoT.Edge.Module.Sdk.Base;
using IIoT.Edge.Module.Sdk.DataPipeline;
using IIoT.Edge.Module.Sdk.Diagnostics;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.DieCutting.Production.Tasks;

/// <summary>
/// 模切设备状态上传任务，只消费 PLC Buffer 状态码，不受主批计划门禁控制。
/// </summary>
internal sealed class DieCuttingDeviceStatusUploadTask : PlcTaskBase
{
    private static readonly TimeSpan DeviceStatusLogInterval = TimeSpan.FromMinutes(1);

    private readonly DieCuttingModuleDefinition _definition;
    private readonly DieCuttingSignalCodec _codec;
    private readonly DieCuttingContext _context;
    private readonly IDataPipelineService _dataPipelineService;
    private readonly IMesUploadDiagnosticsStore _mesDiagnosticsStore;
    private readonly ICloudUploadDiagnosticsStore _cloudDiagnosticsStore;
    private readonly ICloudExecutionPolicy _cloudExecutionPolicy;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> _parameters;
    private readonly DieCuttingModuleOptions _moduleOptions;
    private int _taskLoopInterval;
    private bool _configurationLogged;
    private string? _lastLogKey;
    private DateTimeOffset _lastLoggedAt;
    private MesCallOutcome? _lastOutcome;

    public DieCuttingDeviceStatusUploadTask(
        DieCuttingModuleDefinition definition,
        IPlcBuffer buffer,
        DieCuttingSignalCodec codec,
        DieCuttingContext context,
        IDataPipelineService dataPipelineService,
        IMesUploadDiagnosticsStore mesDiagnosticsStore,
        ICloudUploadDiagnosticsStore cloudDiagnosticsStore,
        ICloudExecutionPolicy cloudExecutionPolicy,
        IPlcConnectionManager plcConnectionManager,
        IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters,
        ILogService logger,
        IOptions<DieCuttingModuleOptions> moduleOptions)
        : base(buffer, context, logger)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dataPipelineService = dataPipelineService ?? throw new ArgumentNullException(nameof(dataPipelineService));
        _mesDiagnosticsStore = mesDiagnosticsStore ?? throw new ArgumentNullException(nameof(mesDiagnosticsStore));
        _cloudDiagnosticsStore = cloudDiagnosticsStore ?? throw new ArgumentNullException(nameof(cloudDiagnosticsStore));
        _cloudExecutionPolicy = cloudExecutionPolicy ?? throw new ArgumentNullException(nameof(cloudExecutionPolicy));
        _plcConnectionManager = plcConnectionManager ?? throw new ArgumentNullException(nameof(plcConnectionManager));
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _moduleOptions = moduleOptions?.Value ?? throw new ArgumentNullException(nameof(moduleOptions));
        _taskLoopInterval = NormalizeInterval(_moduleOptions.Runtime.DataReadLoopIntervalMs, 1000);
    }

    public override string TaskName => _definition.DeviceStatusUploadTaskKey;

    protected override int TaskLoopInterval => _taskLoopInterval;

    protected override async Task DoCoreAsync()
    {
        var parameterSnapshot = await _parameters.GetAsync(TaskCancellationToken).ConfigureAwait(false);
        _taskLoopInterval = NormalizeInterval(
            parameterSnapshot.Business<int>(DieCuttingParams.Business.采集频率毫秒),
            _moduleOptions.Runtime.DataReadLoopIntervalMs);

        LogConfigurationIfNeeded(parameterSnapshot);
        var uploadTargets = DataPipelineUploadTargetPolicy.Resolve(
            parameterSnapshot.Mes<bool>(DieCuttingParams.Mes.启用),
            _cloudExecutionPolicy.IsEnabled);

        var connectionResult = EnsurePlcConnected();
        if (!connectionResult.IsSuccess)
        {
            RecordResult(null, connectionResult, uploadTargets);
            return;
        }

        var statusSnapshot = _codec.CaptureDeviceStatusSnapshot();
        if (!statusSnapshot.IsKnownStatus)
        {
            RecordResult(
                statusSnapshot,
                MesCallResult.InvalidContext($"模切设备状态码未知，已跳过状态上传，状态码={statusSnapshot.StatusCode}。"),
                uploadTargets);
            return;
        }

        if (!HasDeviceStatusChanged(statusSnapshot))
        {
            RecordResult(statusSnapshot, MesCallResult.Success("模切设备状态未变化，已跳过状态上传。"), uploadTargets);
            return;
        }

        if (uploadTargets == DataPipelineUploadTargets.None)
        {
            RecordResult(statusSnapshot, MesCallResult.Disabled("MES/Cloud 上传已关闭，模切设备状态上传已跳过。"), uploadTargets);
            return;
        }

        MesCallResult result;
        try
        {
            var enqueueResult = await _dataPipelineService
                .EnqueueAsync(CreateCompletedRecord(statusSnapshot, uploadTargets), TaskCancellationToken)
                .ConfigureAwait(false);
            result = ModuleDataPipelineEnqueueResultMapper.ToPendingBackgroundUploadResult(
                enqueueResult,
                "模切设备状态",
                uploadTargets);

            if (enqueueResult.IsDurablyAccepted)
            {
                _context.LastDeviceStatusFingerprint = statusSnapshot.CreateFingerprint();
            }
        }
        catch (Exception ex)
        {
            result = MesCallResult.TransportFailure($"模切设备状态处理异常：{ex.Message}");
        }

        RecordResult(statusSnapshot, result, uploadTargets);
    }

    private MesCallResult EnsurePlcConnected()
    {
        var status = _plcConnectionManager.GetRuntimeStatus(_context.NetworkDeviceId);
        if (status?.IsConnected == true)
        {
            return MesCallResult.Success("PLC 已连接。");
        }

        return MesCallResult.InvalidContext("PLC 未连接，模切设备状态上传暂停。");
    }

    private bool HasDeviceStatusChanged(DieCuttingDeviceStatusSnapshot snapshot)
        => !string.Equals(
            _context.LastDeviceStatusFingerprint,
            snapshot.CreateFingerprint(),
            StringComparison.Ordinal);

    private CellCompletedRecord CreateCompletedRecord(
        DieCuttingDeviceStatusSnapshot statusSnapshot,
        DataPipelineUploadTargets uploadTargets)
        => new()
        {
            CellData = new DieCuttingCellData
            {
                ModuleProcessType = _definition.ProcessType,
                DeviceName = _context.DeviceName,
                DeviceCode = _context.DeviceName,
                PlcDeviceId = _context.NetworkDeviceId,
                CellResult = true,
                CompletedTime = statusSnapshot.CapturedAt,
                UploadTargets = uploadTargets,
                RecordKind = DieCuttingCellData.RecordKinds.DeviceStatus,
                CapturedAt = statusSnapshot.CapturedAt,
                WindowStartAt = statusSnapshot.CapturedAt,
                WindowCompleteAt = statusSnapshot.CapturedAt,
                StatusCode = statusSnapshot.StatusCode,
                StatusMessages = statusSnapshot.Messages.ToList()
            },
            NetworkDeviceId = _context.NetworkDeviceId,
            DeviceName = _context.DeviceName,
            ModuleId = _definition.ModuleId,
            TaskKey = TaskName,
            PlanSessionId = string.Empty,
            MainPlanCode = string.Empty,
            TraceBatchNumber = string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        };

    private void RecordResult(
        DieCuttingDeviceStatusSnapshot? snapshot,
        MesCallResult result,
        DataPipelineUploadTargets uploadTargets)
    {
        if (!result.IsSuccess)
        {
            ModuleUploadDiagnosticsRecorder.RecordResult(
                result,
                uploadTargets,
                _mesDiagnosticsStore,
                _cloudDiagnosticsStore,
                new ModuleUploadDiagnosticsRoute(
                    _definition.DeviceStatusDiagnosticsChannel,
                    _definition.ProcessType,
                    "plc_device_status_blocked",
                    "plc_device_status_enqueue_failed"),
                new ModuleUploadDiagnosticsIdentity(
                    _context.DeviceName,
                    _definition.ModuleId,
                    TaskName,
                    "设备状态上传"));
        }

        _context.LastDeviceStatusAt = snapshot?.CapturedAt ?? DateTime.Now;
        _context.LastDeviceStatusResult = result.Message;
        _context.Set($"Runtime.Tasks.{TaskName}.LastUploadOutcome", result.Outcome.ToString());
        _context.Set($"Runtime.Tasks.{TaskName}.LastUploadMessage", result.Message);
        LogResult(snapshot, result);
    }

    private void LogConfigurationIfNeeded(
        ModuleParamSnapshot<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters)
    {
        if (_configurationLogged)
        {
            return;
        }

        var statusPath = NormalizeLogText(parameters.Mes<string>(DieCuttingParams.Mes.EquipmentStatusPath));
        var mesEnabled = parameters.Mes<bool>(DieCuttingParams.Mes.启用);
        var cloudEnabled = _cloudExecutionPolicy.IsEnabled;
        Logger.Info(
            $"[PLC-{_context.DeviceName}][设备状态] 任务配置：设备状态路径={statusPath}，MES启用={mesEnabled}，Cloud启用={cloudEnabled}，采集处理周期={_taskLoopInterval}ms；设备状态独立上传，不受主批计划门禁控制。");
        _configurationLogged = true;
    }

    private void LogResult(DieCuttingDeviceStatusSnapshot? snapshot, MesCallResult result)
    {
        var statusCode = snapshot?.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "无";
        var key = $"{statusCode}|{result.Outcome}|{result.Message}";
        var now = DateTimeOffset.UtcNow;
        if (!ShouldLogOutcome(key, result.Outcome, now))
        {
            return;
        }

        WriteOutcomeLog(result.Outcome, $"[PLC-{_context.DeviceName}][设备状态] 结果：{result.Message} 状态码={statusCode}。");
    }

    private bool ShouldLogOutcome(
        string key,
        MesCallOutcome outcome,
        DateTimeOffset now)
    {
        var isRecovery = _lastOutcome is { } previous
                         && previous is not MesCallOutcome.Success and not MesCallOutcome.Disabled
                         && outcome is MesCallOutcome.Success or MesCallOutcome.Disabled;
        var shouldLog = isRecovery
                        || !string.Equals(_lastLogKey, key, StringComparison.Ordinal)
                        || now - _lastLoggedAt >= DeviceStatusLogInterval;

        _lastOutcome = outcome;
        if (!shouldLog)
        {
            return false;
        }

        _lastLogKey = key;
        _lastLoggedAt = now;
        return true;
    }

    private void WriteOutcomeLog(MesCallOutcome outcome, string message)
    {
        switch (outcome)
        {
            case MesCallOutcome.Success:
            case MesCallOutcome.Disabled:
                Logger.Info(message);
                break;
            case MesCallOutcome.InvalidContext:
                Logger.Warn(message);
                break;
            default:
                Logger.Error(message);
                break;
        }
    }

    private static int NormalizeInterval(int value, int fallback)
    {
        var normalizedFallback = fallback <= 0 ? 1000 : fallback;
        return Math.Max(500, value <= 0 ? normalizedFallback : value);
    }

    private static string NormalizeLogText(string? value)
        => string.IsNullOrWhiteSpace(value) ? "空" : value.Trim();
}
