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
using IIoT.Edge.Module.DieCutting.Mes;
using IIoT.Edge.Module.DieCutting.Payload;
using IIoT.Edge.Module.Sdk.Base;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace IIoT.Edge.Module.DieCutting.Production.Tasks;

/// <summary>
/// 模切实时采样上传任务，定时读取当前 PLC buffer 快照并按配置进入上传链路。
/// </summary>
internal sealed class DieCuttingRealtimeSampleUploadTask : PlcTaskBase
{
    private static readonly TimeSpan RealtimeLogInterval = TimeSpan.FromMinutes(1);

    private readonly DieCuttingModuleDefinition _definition;
    private readonly DieCuttingSignalCodec _codec;
    private readonly DieCuttingContext _context;
    private readonly IDataPipelineService _dataPipelineService;
    private readonly IDieCuttingProductionGate _productionGate;
    private readonly IDieCuttingProductionRecordStore _productionRecordStore;
    private readonly IMesUploadDiagnosticsStore _mesDiagnosticsStore;
    private readonly ICloudUploadDiagnosticsStore _cloudDiagnosticsStore;
    private readonly ICloudExecutionPolicy _cloudExecutionPolicy;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> _parameters;
    private readonly DieCuttingModuleOptions _moduleOptions;
    private int _taskLoopInterval;
    private bool _configurationLogged;
    private bool _legacyMesBaseUrlWarned;
    private string? _lastRealtimeLogKey;
    private DateTimeOffset _lastRealtimeLogAt;
    private MesCallOutcome? _lastRealtimeOutcome;

    /// <summary>
    /// 创建模切实时采样上传任务。
    /// </summary>
    public DieCuttingRealtimeSampleUploadTask(
        DieCuttingModuleDefinition definition,
        IPlcBuffer buffer,
        DieCuttingSignalCodec codec,
        DieCuttingContext context,
        IDataPipelineService dataPipelineService,
        IDieCuttingProductionGate productionGate,
        IDieCuttingProductionRecordStore productionRecordStore,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ICloudUploadDiagnosticsStore cloudDiagnosticsStore,
        ICloudExecutionPolicy cloudExecutionPolicy,
        IPlcConnectionManager plcConnectionManager,
        IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters,
        ILogService logger,
        IOptions<DieCuttingModuleOptions> moduleOptions)
        : base(buffer, context, logger)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _codec = codec;
        _context = context;
        _dataPipelineService = dataPipelineService;
        _productionGate = productionGate;
        _productionRecordStore = productionRecordStore;
        _mesDiagnosticsStore = diagnosticsStore;
        _cloudDiagnosticsStore = cloudDiagnosticsStore;
        _cloudExecutionPolicy = cloudExecutionPolicy;
        _plcConnectionManager = plcConnectionManager;
        _parameters = parameters;
        _moduleOptions = moduleOptions.Value;
        _taskLoopInterval = NormalizeInterval(_moduleOptions.Runtime.DataReadLoopIntervalMs, 1000);
    }

    public override string TaskName => _definition.RealtimeSampleUploadTaskKey;

    protected override int TaskLoopInterval => _taskLoopInterval;

    protected override async Task DoCoreAsync()
    {
        var parameterSnapshot = await _parameters.GetAsync(TaskCancellationToken).ConfigureAwait(false);
        _taskLoopInterval = NormalizeInterval(
            parameterSnapshot.Business<int>(DieCuttingParams.Business.采集频率毫秒),
            _moduleOptions.Runtime.DataReadLoopIntervalMs);
        LogConfigurationIfNeeded(parameterSnapshot);
        WarnIfLegacyMesBaseUrl(parameterSnapshot);
        var mesEnabled = parameterSnapshot.Mes<bool>(DieCuttingParams.Mes.启用);
        var cloudEnabled = _cloudExecutionPolicy.IsEnabled;
        var uploadTargets = DataPipelineUploadTargetPolicy.Resolve(mesEnabled, cloudEnabled);

        var connectionResult = EnsurePlcConnected();
        if (!connectionResult.IsSuccess)
        {
            await RecordResultAsync(null, connectionResult, uploadTargets).ConfigureAwait(false);
            return;
        }

        var identity = _moduleOptions.MesIdentity.Resolve(_context.DeviceName);
        var windowStartAt = _context.NextWindowStartAt ?? DateTime.Now;
        var snapshot = _codec.CaptureRealtimeSnapshot(identity, windowStartAt);
        var outboundFingerprint = snapshot.CreateOutboundFingerprint();
        var outboundChanged = HasOutboundChanged(outboundFingerprint);
        var productionRecordChanged = HasProductionRecordChanged(outboundFingerprint);
        MesCallResult outboundResult;

        if (uploadTargets == DataPipelineUploadTargets.None)
        {
            if (productionRecordChanged && await StoreProductionRecordAsync(snapshot).ConfigureAwait(false))
            {
                _context.LastProductionRecordFingerprint = outboundFingerprint;
            }

            var disabledMessage = productionRecordChanged
                ? "MES/Cloud 上传已关闭，已完成本地模切采样和生产记录更新。"
                : "MES/Cloud 上传已关闭，模切采样快照未变化。";
            await RecordResultAsync(snapshot, MesCallResult.Disabled(disabledMessage), uploadTargets).ConfigureAwait(false);
            return;
        }

        if (!productionRecordChanged && !outboundChanged)
        {
            outboundResult = MesCallResult.Success("模切出站快照未变化，已跳过出站上传。");
            await RecordResultAsync(snapshot, outboundResult, uploadTargets).ConfigureAwait(false);
            return;
        }

        if (mesEnabled)
        {
            var gateResult = await _productionGate.EnsureReadyAsync(_context, TaskCancellationToken).ConfigureAwait(false);
            if (!gateResult.IsSuccess)
            {
                await RecordResultAsync(snapshot, gateResult, uploadTargets).ConfigureAwait(false);
                return;
            }
        }

        if (productionRecordChanged && await StoreProductionRecordAsync(snapshot).ConfigureAwait(false))
        {
            _context.LastProductionRecordFingerprint = outboundFingerprint;
        }

        if (!outboundChanged)
        {
            outboundResult = MesCallResult.Success("模切出站快照未变化，已跳过出站上传。");
            await RecordResultAsync(snapshot, outboundResult, uploadTargets).ConfigureAwait(false);
            return;
        }

        if (outboundChanged)
        {
            var includeMesPlanContext = uploadTargets.HasFlag(DataPipelineUploadTargets.Mes);
            try
            {
                var enqueueResult = await _dataPipelineService
                    .EnqueueAsync(CreateCompletedRecord(
                        snapshot,
                        DieCuttingCellData.RecordKinds.RealtimeOutbound,
                        uploadTargets,
                        includeMesPlanContext), TaskCancellationToken)
                    .ConfigureAwait(false);
                outboundResult = FormatEnqueueResult(enqueueResult, "模切采样", uploadTargets);
                if (enqueueResult.IsDurablyAccepted)
                {
                    _context.LastOutboundFingerprint = outboundFingerprint;
                    _context.NextWindowStartAt = snapshot.WindowCompleteAt;
                }
            }
            catch (Exception ex)
            {
                outboundResult = MesCallResult.TransportFailure($"模切采样处理异常：{ex.Message}");
            }
        }
        else
        {
            outboundResult = MesCallResult.Success("模切出站快照未变化，已跳过出站上传。");
        }

        await RecordResultAsync(snapshot, outboundResult, uploadTargets).ConfigureAwait(false);
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

    private async Task<bool> StoreProductionRecordAsync(DieCuttingRealtimeSnapshot snapshot)
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
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[PLC-{_context.DeviceName}][本地保存] 模切生产数据保存失败: {ex.Message}");
            return false;
        }
    }

    private Task RecordResultAsync(
        DieCuttingRealtimeSnapshot? snapshot,
        MesCallResult result,
        DataPipelineUploadTargets uploadTargets)
    {
        if (!result.IsSuccess)
        {
            RecordUploadDiagnostics(result, uploadTargets);
        }

        _context.LastRealtimeAt = snapshot?.CapturedAt ?? DateTime.Now;
        _context.LastRealtimeResult = result.Message;
        _context.LastRealtimeSnapshot = snapshot;
        _context.Set($"Runtime.Tasks.{TaskName}.LastUploadOutcome", result.Outcome.ToString());
        _context.Set($"Runtime.Tasks.{TaskName}.LastUploadMessage", result.Message);
        LogRealtimeResult(snapshot, result);
        return Task.CompletedTask;
    }

    private bool HasOutboundChanged(string fingerprint)
        => !string.Equals(
            _context.LastOutboundFingerprint,
            fingerprint,
            StringComparison.Ordinal);

    private void RecordMesDiagnostics(string channel, MesCallResult result)
    {
        if (result.Outcome is MesCallOutcome.BusinessRejected or MesCallOutcome.InvalidContext)
        {
            _mesDiagnosticsStore.RecordBlocked(channel, result.Message, CreateMesDiagnosticsContext("生产上传"));
            return;
        }

        _mesDiagnosticsStore.RecordFailure(channel, result.Message, CreateMesDiagnosticsContext("生产上传"));
    }

    private void RecordCloudDiagnostics(MesCallResult result)
    {
        if (result.Outcome is MesCallOutcome.BusinessRejected or MesCallOutcome.InvalidContext)
        {
            _cloudDiagnosticsStore.RecordBlocked(
                _definition.ProcessType,
                "plc_realtime_blocked",
                result.Message,
                CreateCloudDiagnosticsContext("生产上传"));
            return;
        }

        _cloudDiagnosticsStore.RecordResult(
            _definition.ProcessType,
            CloudCallResult.Failure(CloudCallOutcome.Exception, "plc_realtime_enqueue_failed"),
            CreateCloudDiagnosticsContext("生产上传"));
    }

    private void RecordUploadDiagnostics(
        MesCallResult result,
        DataPipelineUploadTargets uploadTargets)
    {
        if (uploadTargets.HasFlag(DataPipelineUploadTargets.Mes))
        {
            RecordMesDiagnostics(_definition.RealtimeDiagnosticsChannel, result);
        }

        if (uploadTargets.HasFlag(DataPipelineUploadTargets.Cloud))
        {
            RecordCloudDiagnostics(result);
        }
    }

    private MesUploadDiagnosticsContext CreateMesDiagnosticsContext(string scenario)
        => new(
            DeviceName: _context.DeviceName,
            ModuleId: _definition.ModuleId,
            TaskKey: TaskName,
            Scenario: scenario);

    private CloudUploadDiagnosticsContext CreateCloudDiagnosticsContext(string scenario)
        => new(
            DeviceName: _context.DeviceName,
            ModuleId: _definition.ModuleId,
            TaskKey: TaskName,
            Scenario: scenario);

    private bool HasProductionRecordChanged(string fingerprint)
        => !string.Equals(
            _context.LastProductionRecordFingerprint,
            fingerprint,
            StringComparison.Ordinal);

    private CellCompletedRecord CreateCompletedRecord(
        DieCuttingRealtimeSnapshot snapshot,
        string recordKind,
        DataPipelineUploadTargets uploadTargets,
        bool includeMesPlanContext)
        => new()
        {
            CellData = new DieCuttingCellData
            {
                ModuleProcessType = _definition.ProcessType,
                DeviceName = _context.DeviceName,
                DeviceCode = snapshot.PunchingDeviceCode,
                PlcDeviceId = _context.NetworkDeviceId,
                CellResult = true,
                CompletedTime = snapshot.WindowCompleteAt,
                UploadTargets = uploadTargets,
                RecordKind = recordKind,
                BatchNumber = snapshot.BatchNumber,
                ClipNo = snapshot.ClipNo,
                ClipNoMg1 = snapshot.ClipNoMg1,
                ClipNoMg2 = snapshot.ClipNoMg2,
                PunchingDeviceCode = snapshot.PunchingDeviceCode,
                PunchingDeviceName = snapshot.PunchingDeviceName,
                PunchingQuantity = snapshot.PunchingQuantity,
                PunchingUom = snapshot.PunchingUom,
                PunchingSpeed = snapshot.PunchingSpeed,
                UnwindingLength = snapshot.UnwindingLength,
                Mg1ReceivingSet = snapshot.Mg1ReceivingSet,
                Mg1ReceivingActual = snapshot.Mg1ReceivingActual,
                Mg2ReceivingSet = snapshot.Mg2ReceivingSet,
                Mg2ReceivingActual = snapshot.Mg2ReceivingActual,
                OkSheetQuantity = snapshot.OkSheetQuantity,
                PlateLengthMm = snapshot.PlateLengthMm,
                PlateWidthMm = snapshot.PlateWidthMm,
                OperatorCode = snapshot.OperatorCode,
                MoldCode = snapshot.MoldCode,
                CutterCode = snapshot.CutterCode,
                CapturedAt = snapshot.CapturedAt,
                WindowStartAt = snapshot.WindowStartAt,
                WindowCompleteAt = snapshot.WindowCompleteAt,
                RawItems = snapshot.RawItems.ToList()
            },
            NetworkDeviceId = _context.NetworkDeviceId,
            DeviceName = _context.DeviceName,
            ModuleId = _definition.ModuleId,
            TaskKey = TaskName,
            PlanSessionId = includeMesPlanContext ? _context.PlanSessionId ?? string.Empty : string.Empty,
            MainPlanCode = includeMesPlanContext ? _context.SelectedProductionPlan?.MainPlanCode ?? string.Empty : string.Empty,
            TraceBatchNumber = includeMesPlanContext ? _context.TraceBatchNumber ?? string.Empty : string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        };

    private static MesCallResult FormatEnqueueResult(
        DataPipelineEnqueueResult enqueueResult,
        string scenarioName,
        DataPipelineUploadTargets uploadTargets)
    {
        if (enqueueResult.IsDurablyAccepted)
        {
            return enqueueResult.WasOverflow
                ? MesCallResult.Success($"{scenarioName}已进入溢出补偿，等待后台上传。")
                : MesCallResult.Success($"{scenarioName}已进入 {DataPipelineUploadTargetPolicy.Format(uploadTargets)} 上传队列，等待后台上传。");
        }

        var reason = string.IsNullOrWhiteSpace(enqueueResult.ReasonCode)
            ? "unknown"
            : enqueueResult.ReasonCode;
        return MesCallResult.TransportFailure($"{scenarioName}未进入上传队列，原因={reason}。");
    }

    private static int NormalizeInterval(int value, int fallback)
    {
        var normalizedFallback = fallback <= 0 ? 1000 : fallback;
        return Math.Max(500, value <= 0 ? normalizedFallback : value);
    }

    private void LogConfigurationIfNeeded(
        ModuleParamSnapshot<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters)
    {
        if (_configurationLogged)
        {
            return;
        }

        var mesBaseUrl = SanitizeMesBaseUrl(parameters.Mes<string>(DieCuttingParams.Mes.服务地址));
        var outboundPath = NormalizeLogText(parameters.Mes<string>(DieCuttingParams.Mes.OutboundPath));
        var mesEnabled = parameters.Mes<bool>(DieCuttingParams.Mes.启用);
        var cloudEnabled = _cloudExecutionPolicy.IsEnabled;
        Logger.Info(
            $"[PLC-{_context.DeviceName}][模切采样] 任务配置：MES地址={mesBaseUrl}，出站路径={outboundPath}，MES启用={mesEnabled}，Cloud启用={cloudEnabled}，采集处理周期={_taskLoopInterval}ms；采集后关键数据变化才进入配置的上传目标。");
        _configurationLogged = true;
    }

    private void WarnIfLegacyMesBaseUrl(
        ModuleParamSnapshot<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters)
    {
        if (_legacyMesBaseUrlWarned)
        {
            return;
        }

        var mesBaseUrl = parameters.Mes<string>(DieCuttingParams.Mes.服务地址);
        if (!_definition.LegacyMesBaseUrls.Contains(mesBaseUrl?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        Logger.Warn(
            $"[PLC-{_context.DeviceName}][参数] 检测到模切 MES 服务地址仍为历史默认值 {SanitizeMesBaseUrl(mesBaseUrl)}，请确认参数已迁移到 {_definition.MesBaseUrl}。");
        _legacyMesBaseUrlWarned = true;
    }

    private void LogRealtimeResult(DieCuttingRealtimeSnapshot? snapshot, MesCallResult result)
    {
        var key = $"{result.Outcome}|{result.Message}";
        var now = DateTimeOffset.UtcNow;
        if (!ShouldLogOutcome(key, result.Outcome, now, ref _lastRealtimeLogKey, ref _lastRealtimeLogAt, ref _lastRealtimeOutcome))
        {
            return;
        }

        var message = snapshot is null
            ? $"[PLC-{_context.DeviceName}][模切采样] 结果：{result.Message}"
            : $"[PLC-{_context.DeviceName}][模切采样] 结果：{result.Message} 批次={NormalizeLogText(snapshot.PunchingLotNumber)}，产量={snapshot.PunchingQuantity}，冲切速度={snapshot.PunchingSpeed}，放卷长度={snapshot.UnwindingLength}，弹夹={NormalizeLogText(snapshot.ClipNo)}。";
        WriteOutcomeLog(result.Outcome, message);
    }

    private static bool ShouldLogOutcome(
        string key,
        MesCallOutcome outcome,
        DateTimeOffset now,
        ref string? lastKey,
        ref DateTimeOffset lastLoggedAt,
        ref MesCallOutcome? lastOutcome)
    {
        var isRecovery = lastOutcome is { } previous
                         && previous is not MesCallOutcome.Success and not MesCallOutcome.Disabled
                         && outcome is MesCallOutcome.Success or MesCallOutcome.Disabled;
        var shouldLog = isRecovery
                        || !string.Equals(lastKey, key, StringComparison.Ordinal)
                        || now - lastLoggedAt >= RealtimeLogInterval;

        lastOutcome = outcome;
        if (!shouldLog)
        {
            return false;
        }

        lastKey = key;
        lastLoggedAt = now;
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

    private static string SanitizeMesBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "未配置";
        }

        var normalized = value.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        var sensitiveIndex = normalized.IndexOfAny(['?', '#']);
        return sensitiveIndex >= 0 ? normalized[..sensitiveIndex] : normalized;
    }

    private static string NormalizeLogText(string? value)
        => string.IsNullOrWhiteSpace(value) ? "空" : value.Trim();

}
