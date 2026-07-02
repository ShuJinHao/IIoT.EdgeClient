using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
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
/// 模切实时采样上传任务，定时读取当前 PLC buffer 快照并上传 MES。
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
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly IModuleParamProvider<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> _parameters;
    private readonly DieCuttingModuleOptions _moduleOptions;
    private int _taskLoopInterval;
    private bool _configurationLogged;
    private bool _legacyMesBaseUrlWarned;
    private string? _lastRealtimeLogKey;
    private DateTimeOffset _lastRealtimeLogAt;
    private MesCallOutcome? _lastRealtimeOutcome;
    private string? _lastDeviceStatusLogKey;
    private DateTimeOffset _lastDeviceStatusLogAt;
    private MesCallOutcome? _lastDeviceStatusOutcome;

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
        _diagnosticsStore = diagnosticsStore;
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
        var freshnessTimeoutMs = ResolveFreshnessTimeout(
            _taskLoopInterval,
            _moduleOptions.Runtime.DataFreshnessTimeoutMs);

        LogConfigurationIfNeeded(parameterSnapshot, freshnessTimeoutMs);
        WarnIfLegacyMesBaseUrl(parameterSnapshot);

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
        var outboundFingerprint = snapshot.CreateOutboundFingerprint();
        var outboundChanged = HasOutboundChanged(outboundFingerprint);
        var productionRecordChanged = HasProductionRecordChanged(outboundFingerprint);
        var statusChanged = HasDeviceStatusChanged(statusSnapshot);
        var shouldUploadDeviceStatus = statusChanged && statusSnapshot.IsKnownStatus;
        MesCallResult outboundResult;

        if (!parameterSnapshot.Mes<bool>(DieCuttingParams.Mes.启用))
        {
            if (productionRecordChanged && await StoreProductionRecordAsync(snapshot).ConfigureAwait(false))
            {
                _context.LastProductionRecordFingerprint = outboundFingerprint;
            }

            var disabledMessage = productionRecordChanged
                ? "MES 上传已关闭，已完成本地模切采样和生产记录更新。"
                : "MES 上传已关闭，模切采样快照未变化。";
            await RecordResultAsync(snapshot, MesCallResult.Disabled(disabledMessage)).ConfigureAwait(false);
            return;
        }

        if (!productionRecordChanged && !outboundChanged && !statusChanged)
        {
            outboundResult = MesCallResult.Success("模切出站快照未变化，已跳过出站上传。");
            await RecordResultAsync(snapshot, outboundResult).ConfigureAwait(false);
            return;
        }

        var gateResult = await _productionGate.EnsureReadyAsync(_context, TaskCancellationToken).ConfigureAwait(false);
        if (!gateResult.IsSuccess)
        {
            await RecordResultAsync(snapshot, gateResult).ConfigureAwait(false);
            if (shouldUploadDeviceStatus)
            {
                RecordDeviceStatusResult(gateResult);
            }

            return;
        }

        if (productionRecordChanged && await StoreProductionRecordAsync(snapshot).ConfigureAwait(false))
        {
            _context.LastProductionRecordFingerprint = outboundFingerprint;
        }

        MesCallResult? statusResult = null;
        if (statusChanged && !statusSnapshot.IsKnownStatus)
        {
            statusResult = MesCallResult.InvalidContext($"模切设备状态码未知，已跳过状态上传，状态码={statusSnapshot.StatusCode}。");
            _context.LastDeviceStatusFingerprint = statusSnapshot.CreateFingerprint();
            RecordDeviceStatusResult(statusResult);
        }

        if (!outboundChanged && !shouldUploadDeviceStatus)
        {
            outboundResult = MesCallResult.Success("模切出站快照未变化，已跳过出站上传。");
            await RecordResultAsync(snapshot, statusResult ?? outboundResult).ConfigureAwait(false);
            return;
        }

        if (outboundChanged)
        {
            var enqueueResult = await _dataPipelineService
                .EnqueueAsync(CreateCompletedRecord(snapshot, DieCuttingCellData.RecordKinds.RealtimeOutbound), TaskCancellationToken)
                .ConfigureAwait(false);
            outboundResult = FormatEnqueueResult(enqueueResult, "模切采样");
            if (enqueueResult.IsDurablyAccepted)
            {
                _context.LastOutboundFingerprint = outboundFingerprint;
                _context.NextWindowStartAt = snapshot.WindowCompleteAt;
            }
        }
        else
        {
            outboundResult = MesCallResult.Success("模切出站快照未变化，已跳过出站上传。");
        }

        if (shouldUploadDeviceStatus)
        {
            var enqueueResult = await _dataPipelineService
                .EnqueueAsync(CreateCompletedRecord(snapshot, DieCuttingCellData.RecordKinds.DeviceStatus, statusSnapshot), TaskCancellationToken)
                .ConfigureAwait(false);
            statusResult = FormatEnqueueResult(enqueueResult, "模切设备状态");

            if (enqueueResult.IsDurablyAccepted)
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

    private Task RecordResultAsync(DieCuttingRealtimeSnapshot? snapshot, MesCallResult result)
    {
        if (!result.IsSuccess)
        {
            _diagnosticsStore.RecordFailure(_definition.RealtimeDiagnosticsChannel, result.Message);
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

    private bool HasProductionRecordChanged(string fingerprint)
        => !string.Equals(
            _context.LastProductionRecordFingerprint,
            fingerprint,
            StringComparison.Ordinal);

    private bool HasDeviceStatusChanged(DieCuttingDeviceStatusSnapshot snapshot)
        => !string.Equals(
            _context.LastDeviceStatusFingerprint,
            snapshot.CreateFingerprint(),
            StringComparison.Ordinal);

    private void RecordDeviceStatusResult(MesCallResult result)
    {
        if (!result.IsSuccess)
        {
            _diagnosticsStore.RecordFailure(_definition.DeviceStatusDiagnosticsChannel, result.Message);
        }

        _context.Set($"Runtime.Tasks.{TaskName}.LastDeviceStatusOutcome", result.Outcome.ToString());
        _context.Set($"Runtime.Tasks.{TaskName}.LastDeviceStatusMessage", result.Message);
        LogDeviceStatusResult(result);
    }

    private CellCompletedRecord CreateCompletedRecord(
        DieCuttingRealtimeSnapshot snapshot,
        string recordKind,
        DieCuttingDeviceStatusSnapshot? statusSnapshot = null)
        => new()
        {
            CellData = new DieCuttingCellData
            {
                ModuleProcessType = _definition.ProcessType,
                DeviceName = _context.DeviceName,
                DeviceCode = snapshot.PunchingDeviceCode,
                PlcDeviceId = _context.NetworkDeviceId,
                CellResult = true,
                CompletedTime = statusSnapshot?.CapturedAt ?? snapshot.WindowCompleteAt,
                UploadTargets = DataPipelineUploadTargets.Mes,
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
                RawItems = snapshot.RawItems.ToList(),
                StatusCode = statusSnapshot?.StatusCode,
                StatusMessages = statusSnapshot?.Messages.ToList() ?? []
            },
            NetworkDeviceId = _context.NetworkDeviceId,
            DeviceName = _context.DeviceName,
            ModuleId = _definition.ModuleId,
            TaskKey = TaskName,
            PlanSessionId = _context.PlanSessionId ?? string.Empty,
            MainPlanCode = _context.SelectedProductionPlan?.MainPlanCode ?? string.Empty,
            TraceBatchNumber = _context.TraceBatchNumber ?? string.Empty,
            CreatedAtUtc = DateTime.UtcNow
        };

    private static MesCallResult FormatEnqueueResult(
        DataPipelineEnqueueResult enqueueResult,
        string scenarioName)
    {
        if (enqueueResult.IsDurablyAccepted)
        {
            return enqueueResult.WasOverflow
                ? MesCallResult.Success($"{scenarioName}已进入溢出补偿，等待后台上传。")
                : MesCallResult.Success($"{scenarioName}已进入 MES 上传队列，等待后台上传。");
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

    private static int ResolveFreshnessTimeout(int taskLoopIntervalMs, int configured)
        => Math.Max(
            Math.Max(taskLoopIntervalMs * 3, 3000),
            configured <= 0 ? 0 : configured);

    private void LogConfigurationIfNeeded(
        ModuleParamSnapshot<DieCuttingParams.Mes, DieCuttingParams.Cloud, DieCuttingParams.Business> parameters,
        int freshnessTimeoutMs)
    {
        if (_configurationLogged)
        {
            return;
        }

        var mesBaseUrl = SanitizeMesBaseUrl(parameters.Mes<string>(DieCuttingParams.Mes.服务地址));
        var outboundPath = NormalizeLogText(parameters.Mes<string>(DieCuttingParams.Mes.OutboundPath));
        var statusPath = NormalizeLogText(parameters.Mes<string>(DieCuttingParams.Mes.EquipmentStatusPath));
        Logger.Info(
            $"[PLC-{_context.DeviceName}][模切采样] 任务配置：MES地址={mesBaseUrl}，出站路径={outboundPath}，设备状态路径={statusPath}，采集处理周期={_taskLoopInterval}ms，新鲜度保护={freshnessTimeoutMs}ms；采集后关键数据变化才上传 MES。");
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

    private void LogDeviceStatusResult(MesCallResult result)
    {
        var key = $"{result.Outcome}|{result.Message}";
        var now = DateTimeOffset.UtcNow;
        if (!ShouldLogOutcome(
                key,
                result.Outcome,
                now,
                ref _lastDeviceStatusLogKey,
                ref _lastDeviceStatusLogAt,
                ref _lastDeviceStatusOutcome))
        {
            return;
        }

        WriteOutcomeLog(result.Outcome, $"[PLC-{_context.DeviceName}][设备状态] 上传结果：{result.Message}");
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
