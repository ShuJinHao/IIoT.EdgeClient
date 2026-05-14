using System.Collections.ObjectModel;
using IIoT.Edge.Application.Abstractions.Device;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Diagnostics;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed partial class DiagnosticsViewModel : NavigationPageViewModelBase
{
    private readonly IServiceProvider _services;
    private readonly AsyncRelayCommand _refreshCommand;

    public DiagnosticsViewModel(
        IServiceProvider services,
        IAvaloniaLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _services = services;
        _refreshCommand = new AsyncRelayCommand(RefreshAsync);
        FeedbackMessage = "诊断页只读取当前注册、启动与持久化状态，不执行死信清理或重试。";
    }

    public ObservableCollection<RuntimeRegistrationRow> RuntimeRegistrations { get; } = [];

    public ObservableCollection<DiagnosticsModuleRegistrationRow> ModuleRegistrations { get; } = [];

    public ObservableCollection<DiagnosticsPersistenceRow> PersistenceRows { get; } = [];

    public ObservableCollection<DiagnosticsPluginStateRow> PluginStates { get; } = [];

    public ObservableCollection<DiagnosticsIssueRow> Issues { get; } = [];

    public ObservableCollection<DiagnosticsIoWriteGateRow> IoWriteGateRows { get; } = [];

    public ObservableCollection<DiagnosticsPlcWriteTraceRow> PlcWriteTraceRows { get; } = [];

    public ObservableCollection<DiagnosticsFieldAcceptanceSummaryRow> FieldAcceptanceRows { get; } = [];

    [ObservableProperty]
    private string lastGeneratedText = "启动诊断尚未生成。";

    [ObservableProperty]
    private string configurationProfileText = "配置概况：-";

    [ObservableProperty]
    private string detailText = "等待刷新。";

    [ObservableProperty]
    private string feedbackMessage = string.Empty;

    public IAsyncRelayCommand RefreshCommand => _refreshCommand;

    public override Task OnActivatedAsync()
        => RefreshAsync();

    internal async Task RefreshAsync()
    {
        try
        {
            var report = ResolveOptional<IStartupDiagnosticsStore>()?.Current ?? StartupDiagnosticsReport.Empty();

            ApplyRuntimeRegistrations(ResolveOptional<IStationRuntimeRegistry>()?.GetRegistrations());
            ApplyModuleRegistrations(report.ModuleRegistrations);
            var syncDiagnostics = await ApplyPersistenceRowsAsync();
            ApplyPluginStates(report.PluginStates);
            ApplyIssues(report.Issues);
            ApplyIoWriteGateRows();
            ApplyPlcWriteTraceRows();
            ApplyFieldAcceptanceSummary(report, syncDiagnostics);

            ConfigurationProfileText = BuildConfigurationProfileText(report.ConfigurationProfile);
            LastGeneratedText = report.GeneratedAt == DateTime.MinValue
                ? "启动诊断尚未生成。"
                : $"最近生成：{report.GeneratedAt:yyyy-MM-dd HH:mm:ss}";
            DetailText = BuildDetailText(report, syncDiagnostics);
            FeedbackMessage = $"已刷新：运行时注册 {RuntimeRegistrations.Count} 个，模块注册 {ModuleRegistrations.Count} 个，问题 {Issues.Count} 个。";
        }
        catch (Exception ex)
        {
            FeedbackMessage = $"诊断读取失败：{ex.Message}";
        }
    }

    private void ApplyRuntimeRegistrations(IReadOnlyDictionary<string, IStationRuntimeFactory>? registrations)
    {
        if (registrations is null)
        {
            Replace(RuntimeRegistrations, [new RuntimeRegistrationRow("诊断服务", 0, "运行时注册表未接入当前容器。")]);
            return;
        }

        var rows = registrations
            .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static item =>
            {
                var candidates = item.Value.GetTaskCandidates()
                    .OrderBy(static candidate => candidate.Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var taskNames = candidates.Length == 0
                    ? "未声明任务"
                    : string.Join("；", candidates.Select(static candidate => candidate.DisplayName));
                return new RuntimeRegistrationRow(item.Key, candidates.Length, taskNames);
            })
            .ToArray();

        Replace(RuntimeRegistrations, rows);
    }

    private void ApplyModuleRegistrations(IReadOnlyList<ModuleRegistrationSnapshot> registrations)
    {
        var rows = registrations
            .OrderBy(static item => item.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(static item => new DiagnosticsModuleRegistrationRow(
                item.ModuleId,
                item.ProcessType,
                item.AssemblyName,
                FormatEnabled(item.IsEnabled),
                FormatRegistered(item.HasCellDataRegistration),
                FormatRegistered(item.HasRuntimeFactory),
                FormatRegistered(item.HasCloudUploader),
                FormatRegistered(item.HasMesUploader),
                FormatRegistered(item.HasHardwareProfile)))
            .ToArray();

        Replace(ModuleRegistrations, rows);
    }

    private async Task<EdgeSyncDiagnosticsSnapshot?> ApplyPersistenceRowsAsync()
    {
        var syncDiagnosticsQuery = ResolveOptional<IEdgeSyncDiagnosticsQuery>();
        if (syncDiagnosticsQuery is null)
        {
            Replace(PersistenceRows, [new DiagnosticsPersistenceRow("同步诊断", "未接入", "当前容器未注册同步诊断查询服务。")]);
            return null;
        }

        var snapshot = await syncDiagnosticsQuery.GetCurrentAsync();
        var context = snapshot.ContextPersistence;
        var cloudDeadLetters = snapshot.Cloud.DeadLetters ?? DeadLetterDiagnosticsSnapshot.Empty;
        var mesDeadLetters = snapshot.Mes.DeadLetters ?? DeadLetterDiagnosticsSnapshot.Empty;
        var cloudStatus = EdgeSyncDiagnosticStatusClassifier.ClassifyCloud(snapshot.Cloud);
        var mesStatus = EdgeSyncDiagnosticStatusClassifier.ClassifyMes(snapshot.Mes);

        var rows = new[]
        {
            new DiagnosticsPersistenceRow(
                "生产上下文持久化",
                context.CorruptFileCount == 0 ? "正常" : "异常",
                context.CorruptFileCount == 0
                    ? "未发现坏档。"
                    : $"坏档数量：{context.CorruptFileCount}；最近发现：{FormatTime(context.LastCorruptDetectedAt)}"),
            new DiagnosticsPersistenceRow(
                "云端补传持久化",
                snapshot.Cloud.IsPersistenceFaulted ? "异常" : cloudStatus.ToString(),
                snapshot.Cloud.IsPersistenceFaulted
                    ? $"访问失败：{snapshot.Cloud.PersistenceFaultMessage}"
                    : $"待补传：生产 {snapshot.Cloud.PendingPassStationCount}，日志 {snapshot.Cloud.PendingDeviceLogCount}，产能 {snapshot.Cloud.PendingCapacityCount}；死信 {cloudDeadLetters.TotalCount}。"),
            new DiagnosticsPersistenceRow(
                "MES 补传持久化",
                snapshot.Mes.IsPersistenceFaulted ? "异常" : mesStatus.ToString(),
                snapshot.Mes.IsPersistenceFaulted
                    ? $"访问失败：{snapshot.Mes.PersistenceFaultMessage}"
                    : $"待补传：{snapshot.Mes.PendingRetryCount}；死信 {mesDeadLetters.TotalCount}。")
        };

        Replace(PersistenceRows, rows);
        return snapshot;
    }

    private void ApplyPluginStates(IReadOnlyList<PluginLifecycleSnapshot> states)
    {
        var rows = states
            .OrderBy(static item => item.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(static item => new DiagnosticsPluginStateRow(
                item.ModuleId,
                item.DisplayName,
                item.ProcessType ?? string.Empty,
                item.Version,
                item.State.ToString(),
                item.Message))
            .ToArray();

        Replace(PluginStates, rows);
    }

    private void ApplyIssues(IReadOnlyList<StartupDiagnosticIssue> issues)
    {
        var rows = issues
            .Select(static item => new DiagnosticsIssueRow(
                item.Code,
                item.ModuleId ?? string.Empty,
                item.DeviceName ?? string.Empty,
                item.Message))
            .ToArray();

        Replace(Issues, rows);
    }

    private void ApplyIoWriteGateRows()
    {
        var auditStore = ResolveOptional<IIoViewWriteGateAuditStore>();
        if (auditStore is null)
        {
            Replace(IoWriteGateRows, [new DiagnosticsIoWriteGateRow("--", "写入闸门", "未接入", "当前容器未注册 I/O 写入闸门审计。", "--")]);
            return;
        }

        var rows = auditStore.GetRecent()
            .Select(static item => new DiagnosticsIoWriteGateRow(
                item.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                item.DeviceName,
                item.BusinessGroup,
                item.Message,
                item.Value?.ToString() ?? "--"))
            .ToArray();

        Replace(IoWriteGateRows, rows.Length == 0
            ? [new DiagnosticsIoWriteGateRow("--", "I/O", "暂无申请", "本次启动尚未发生 I/O 写入申请。", "--")]
            : rows);
    }

    private void ApplyPlcWriteTraceRows()
    {
        var traceStore = ResolveOptional<IPlcIoWriteTraceStore>();
        if (traceStore is null)
        {
            Replace(PlcWriteTraceRows, [new DiagnosticsPlcWriteTraceRow("--", "PLC 写入轨迹", "未接入", "--", "--", "当前容器未注册 PLC 写入轨迹存储。")]);
            return;
        }

        var rows = traceStore.GetRecent()
            .Select(static item => new DiagnosticsPlcWriteTraceRow(
                item.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                item.DeviceName,
                FormatTraceKind(item.Kind),
                item.StartAddress,
                item.WordCount.ToString(),
                string.IsNullOrWhiteSpace(item.ErrorMessage)
                    ? string.Join("、", item.SignalKeys)
                    : $"{string.Join("、", item.SignalKeys)}；原因：{item.ErrorMessage}"))
            .ToArray();

        Replace(PlcWriteTraceRows, rows.Length == 0
            ? [new DiagnosticsPlcWriteTraceRow("--", "PLC", "暂无轨迹", "--", "--", "本次启动尚未记录 PLC 块写入。")]
            : rows);
    }

    private void ApplyFieldAcceptanceSummary(
        StartupDiagnosticsReport report,
        EdgeSyncDiagnosticsSnapshot? diagnostics)
    {
        var runtimeState = ResolveOptional<IAvaloniaRuntimeState>()?.Snapshot;
        var runtimeStatus = runtimeState is null ? "未接入" : FormatRuntimeModeStatus(runtimeState);
        var runtimeMessage = runtimeState is null
            ? "当前容器未注册 Avalonia 运行状态服务。"
            : BuildRuntimeModeMessage(runtimeState);

        var latestIo = ResolveOptional<IIoViewWriteGateAuditStore>()?.GetRecent(1).FirstOrDefault();
        var latestIoStatus = latestIo is null ? "暂无申请" : FormatIoWriteKind(latestIo.Kind);
        var latestIoMessage = latestIo is null
            ? "本次启动尚未发生 I/O 写入申请。"
            : $"时间：{FormatTime(latestIo.OccurredAt)}；设备：{latestIo.DeviceName}；业务组：{latestIo.BusinessGroup}；写入值：{latestIo.Value?.ToString() ?? "--"}；{latestIo.Message}";

        var latestTrace = ResolveOptional<IPlcIoWriteTraceStore>()?.GetRecent(1).FirstOrDefault();
        var latestTraceStatus = latestTrace is null ? "暂无轨迹" : FormatTraceKind(latestTrace.Kind);
        var latestTraceMessage = latestTrace is null
            ? "本次启动尚未记录 PLC 块写入轨迹。"
            : BuildPlcTraceSummary(latestTrace);

        var cloudStatus = diagnostics is null
            ? "未接入"
            : FormatCloudDiagnosticStatus(EdgeSyncDiagnosticStatusClassifier.ClassifyCloud(diagnostics.Cloud));
        var cloudMessage = diagnostics is null
            ? "当前容器未注册同步诊断查询服务。"
            : BuildCloudReadonlySummary(diagnostics.Cloud);

        var mesStatus = diagnostics is null
            ? "未接入"
            : FormatMesDiagnosticStatus(EdgeSyncDiagnosticStatusClassifier.ClassifyMes(diagnostics.Mes));
        var mesMessage = diagnostics is null
            ? "当前容器未注册同步诊断查询服务。"
            : BuildMesReadonlySummary(diagnostics.Mes);

        Replace(FieldAcceptanceRows,
        [
            new DiagnosticsFieldAcceptanceSummaryRow("运行模式", runtimeStatus, runtimeMessage),
            new DiagnosticsFieldAcceptanceSummaryRow(
                "启动诊断",
                report.GeneratedAt == DateTime.MinValue ? "未生成" : "已生成",
                $"模块 {report.ModuleRegistrations.Count} 个；PLC 设备 {report.DeviceBindings.Count} 个；阻断问题 {report.Issues.Count} 个；运行目录 {report.ConfigurationProfile.RuntimeDataRoot}。"),
            new DiagnosticsFieldAcceptanceSummaryRow("I/O 写入申请", latestIoStatus, latestIoMessage),
            new DiagnosticsFieldAcceptanceSummaryRow("PLC 块写入轨迹", latestTraceStatus, latestTraceMessage),
            new DiagnosticsFieldAcceptanceSummaryRow("Cloud 状态", cloudStatus, cloudMessage),
            new DiagnosticsFieldAcceptanceSummaryRow("MES 状态", mesStatus, mesMessage)
        ]);
    }

    private static string BuildConfigurationProfileText(ConfigurationProfileSnapshot profile)
    {
        var machine = string.IsNullOrWhiteSpace(profile.MachineProfile)
            ? "未配置"
            : profile.MachineProfile;
        var loaded = profile.IsMachineProfileLoaded ? "已加载" : "未加载";
        return $"环境：{profile.EnvironmentName}；机型：{machine}（{loaded}）；运行目录：{profile.RuntimeDataRoot}";
    }

    private static string BuildDetailText(
        StartupDiagnosticsReport report,
        EdgeSyncDiagnosticsSnapshot? diagnostics)
    {
        var profile = report.ConfigurationProfile;
        var cloud = diagnostics is null
            ? "云端同步：未接入"
            : $"云端同步：{diagnostics.Cloud.RuntimeState}，闸门 {diagnostics.Cloud.GateState}，待补传 {diagnostics.Cloud.PendingRetryCount + diagnostics.Cloud.PendingPassStationCount + diagnostics.Cloud.PendingDeviceLogCount + diagnostics.Cloud.PendingCapacityCount}";
        var mes = diagnostics is null
            ? "MES 同步：未接入"
            : $"MES 同步：{diagnostics.Mes.RuntimeState}，待补传 {diagnostics.Mes.PendingRetryCount}";

        return $"运行目录：{profile.RuntimeDataRoot}；模块 {report.ModuleRegistrations.Count} 个；插件 {report.PluginStates.Count} 个；{cloud}；{mes}";
    }

    private static string FormatRegistered(bool value) => value ? "已注册" : "未注册";

    private static string FormatEnabled(bool value) => value ? "已启用" : "未启用";

    private static string FormatTime(DateTime? value)
        => value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm:ss") : "--";

    private static string FormatBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? "--" : value;

    private static string FormatTime(DateTimeOffset value)
        => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private static string FormatRuntimeModeStatus(AvaloniaRuntimeStateSnapshot snapshot)
        => snapshot.Status switch
        {
            AvaloniaRuntimeStatus.UiOnly => "UI-only",
            AvaloniaRuntimeStatus.Running => "--start-runtime",
            _ => snapshot.StatusText
        };

    private static string BuildRuntimeModeMessage(AvaloniaRuntimeStateSnapshot snapshot)
    {
        var modeMessage = snapshot.Status switch
        {
            AvaloniaRuntimeStatus.UiOnly => "当前为 UI-only，运行链路未启动；需要现场联调时应从运行联调入口或传入 --start-runtime。",
            AvaloniaRuntimeStatus.Starting => "已接收到 --start-runtime，运行链路正在启动；诊断页只读展示当前启动过程。",
            AvaloniaRuntimeStatus.Running => "已通过 --start-runtime 启动运行链路；诊断页只读展示状态，不执行清理、重试或写入。",
            AvaloniaRuntimeStatus.StartFailed => "已尝试 --start-runtime，但启动失败；应保留诊断信息，不继续 I/O 写入申请。",
            AvaloniaRuntimeStatus.Stopping => "运行链路正在停机；诊断页只读展示最后状态。",
            _ => "运行链路状态未知；诊断页只读展示当前快照。"
        };

        return JoinSummary(
            modeMessage,
            string.IsNullOrWhiteSpace(snapshot.DetailText) ? null : $"状态详情：{snapshot.DetailText}",
            string.IsNullOrWhiteSpace(snapshot.DiagnosticsSummary) ? null : $"启动摘要：{snapshot.DiagnosticsSummary}",
            string.IsNullOrWhiteSpace(snapshot.DiagnosticsLogPath) ? null : $"诊断日志：{snapshot.DiagnosticsLogPath}");
    }

    private static string BuildPlcTraceSummary(PlcIoWriteTraceEntry trace)
    {
        var signalText = trace.SignalKeys.Count == 0
            ? "--"
            : string.Join("、", trace.SignalKeys);

        return JoinSummary(
            $"时间：{FormatTime(trace.OccurredAt)}",
            $"设备：{trace.DeviceName}",
            $"块：{trace.StartAddress} / {trace.WordCount} 字",
            $"信号：{signalText}",
            string.IsNullOrWhiteSpace(trace.ErrorMessage) ? null : $"原因：{trace.ErrorMessage}");
    }

    private static string BuildCloudReadonlySummary(CloudSyncDiagnosticsSnapshot cloud)
    {
        var pendingCount = cloud.PendingRetryCount
            + cloud.PendingPassStationCount
            + cloud.PendingDeviceLogCount
            + cloud.PendingCapacityCount;

        return JoinSummary(
            $"运行：{FormatCloudRuntimeState(cloud.RuntimeState)}",
            $"闸门：{FormatCloudGateState(cloud.GateState)}",
            $"待补传：{pendingCount}",
            $"死信：{cloud.DeadLetters?.TotalCount ?? 0}",
            $"最近结果：{FormatCloudOutcome(cloud.LastOutcome)}",
            cloud.IsPersistenceFaulted ? $"持久化故障：{cloud.PersistenceFaultMessage}" : null);
    }

    private static string BuildMesReadonlySummary(MesSyncDiagnosticsSnapshot mes)
        => JoinSummary(
            $"运行：{FormatMesRuntimeState(mes.RuntimeState)}",
            $"待补传：{mes.PendingRetryCount}",
            $"死信：{mes.DeadLetters?.TotalCount ?? 0}",
            $"最近失败：{FormatTime(mes.LastFailureAt)}",
            mes.IsPersistenceFaulted ? $"持久化故障：{mes.PersistenceFaultMessage}" : null);

    private static string FormatTraceKind(PlcIoWriteTraceKind kind)
        => kind switch
        {
            PlcIoWriteTraceKind.Attempt => "尝试",
            PlcIoWriteTraceKind.Success => "成功",
            PlcIoWriteTraceKind.Failed => "失败",
            _ => kind.ToString()
        };

    private static string FormatIoWriteKind(IoViewWriteResultKind kind)
        => kind switch
        {
            IoViewWriteResultKind.AcceptedToRuntimeBuffer => "已进入运行时缓冲",
            IoViewWriteResultKind.RuntimeNotStarted => "运行链路未启动",
            IoViewWriteResultKind.NoPermission => "权限不足",
            IoViewWriteResultKind.DeviceNotBound => "设备未绑定",
            IoViewWriteResultKind.PlcDisconnected => "PLC 未连接",
            IoViewWriteResultKind.NoWritableSignal => "无可写信号",
            IoViewWriteResultKind.InvalidValue => "写入值无效",
            IoViewWriteResultKind.RejectedByUser => "用户取消",
            IoViewWriteResultKind.BufferUnavailable => "运行时缓冲不可用",
            _ => kind.ToString()
        };

    private static string FormatCloudDiagnosticStatus(CloudSyncDiagnosticStatus status)
        => status switch
        {
            CloudSyncDiagnosticStatus.PersistenceFaulted => "存储故障",
            CloudSyncDiagnosticStatus.CapacityBlocked => "产能阻塞",
            CloudSyncDiagnosticStatus.WaitingHeartbeat => "等待心跳",
            CloudSyncDiagnosticStatus.Ready => "已就绪",
            CloudSyncDiagnosticStatus.WaitingRecovery => "等待恢复",
            CloudSyncDiagnosticStatus.Blocked => "已阻断",
            _ => status.ToString()
        };

    private static string FormatMesDiagnosticStatus(MesSyncDiagnosticStatus status)
        => status switch
        {
            MesSyncDiagnosticStatus.PersistenceFaulted => "存储故障",
            MesSyncDiagnosticStatus.CapacityBlocked => "产能阻塞",
            MesSyncDiagnosticStatus.WaitingHeartbeat => "等待心跳",
            MesSyncDiagnosticStatus.Retrying => "重试中",
            MesSyncDiagnosticStatus.Backoff => "退避中",
            MesSyncDiagnosticStatus.LastFailed => "最近失败",
            MesSyncDiagnosticStatus.Idle => "空闲",
            _ => status.ToString()
        };

    private static string FormatCloudRuntimeState(CloudRetryRuntimeState state)
        => state switch
        {
            CloudRetryRuntimeState.Idle => "空闲",
            CloudRetryRuntimeState.Retrying => "重试中",
            CloudRetryRuntimeState.Backoff => "退避中",
            CloudRetryRuntimeState.WaitingForRecovery => "等待恢复",
            _ => state.ToString()
        };

    private static string FormatMesRuntimeState(MesRetryRuntimeState state)
        => state switch
        {
            MesRetryRuntimeState.Idle => "空闲",
            MesRetryRuntimeState.Retrying => "重试中",
            MesRetryRuntimeState.Backoff => "退避中",
            MesRetryRuntimeState.LastFailed => "最近失败",
            _ => state.ToString()
        };

    private static string FormatCloudGateState(EdgeUploadGateState state)
        => state switch
        {
            EdgeUploadGateState.Unknown => "未知",
            EdgeUploadGateState.Refreshing => "刷新中",
            EdgeUploadGateState.Ready => "已就绪",
            EdgeUploadGateState.Blocked => "已阻断",
            _ => state.ToString()
        };

    private static string FormatCloudOutcome(CloudCallOutcome outcome)
        => outcome switch
        {
            CloudCallOutcome.Success => "成功",
            CloudCallOutcome.SkippedUploadNotReady => "上传闸门未就绪",
            CloudCallOutcome.UnauthorizedAfterRetry => "鉴权失败",
            CloudCallOutcome.HttpFailure => "HTTP 失败",
            CloudCallOutcome.NetworkFailure => "网络失败",
            CloudCallOutcome.Exception => "异常",
            _ => outcome.ToString()
        };

    private static string JoinSummary(params string?[] segments)
        => string.Join("；", segments.Where(static segment => !string.IsNullOrWhiteSpace(segment)));

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private T? ResolveOptional<T>()
        where T : class
    {
        try
        {
            return _services.GetService<T>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

public sealed record RuntimeRegistrationRow(string ModuleId, int TaskCount, string TaskNames);

public sealed record DiagnosticsModuleRegistrationRow(
    string ModuleId,
    string ProcessType,
    string AssemblyName,
    string EnabledText,
    string CellDataText,
    string RuntimeFactoryText,
    string CloudUploaderText,
    string MesUploaderText,
    string HardwareProfileText);

public sealed record DiagnosticsPersistenceRow(string Scope, string Status, string Message);

public sealed record DiagnosticsPluginStateRow(
    string ModuleId,
    string DisplayName,
    string ProcessType,
    string Version,
    string State,
    string Message);

public sealed record DiagnosticsIssueRow(string Code, string ModuleId, string DeviceName, string Message);

public sealed record DiagnosticsIoWriteGateRow(
    string Time,
    string DeviceName,
    string BusinessGroup,
    string Message,
    string Value);

public sealed record DiagnosticsPlcWriteTraceRow(
    string Time,
    string DeviceName,
    string Kind,
    string StartAddress,
    string WordCount,
    string Message);

public sealed record DiagnosticsFieldAcceptanceSummaryRow(
    string Scope,
    string Status,
    string Message);
