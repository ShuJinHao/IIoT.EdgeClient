using System.Collections.ObjectModel;
using IIoT.Edge.Application.Abstractions.Auth;
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
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IEdgeSyncDiagnosticStatusClassifier _statusClassifier;
    private readonly AsyncRelayCommand _refreshCommand;
    private readonly AsyncRelayCommand<DiagnosticsDeadLetterRow> _requeueDeadLetterCommand;
    private readonly AsyncRelayCommand<DiagnosticsDeadLetterRow> _deleteDeadLetterCommand;
    private readonly IAvaloniaDiagnosticsDeadLetterOperator? _deadLetterOperator;
    private readonly IAvaloniaDiagnosticsDeadLetterConfirmationService? _deadLetterConfirmationService;
    private readonly IClientPermissionService? _permissionService;
    private bool _isObservingPermission;

    public DiagnosticsViewModel(
        IServiceProvider services,
        IAvaloniaLanguageService languageService,
        IEdgeSyncDiagnosticStatusClassifier statusClassifier,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _services = services;
        _languageService = languageService;
        _statusClassifier = statusClassifier ?? throw new ArgumentNullException(nameof(statusClassifier));
        _refreshCommand = new AsyncRelayCommand(RefreshAsync);
        _deadLetterOperator = services.GetService<IAvaloniaDiagnosticsDeadLetterOperator>();
        _deadLetterConfirmationService = services.GetService<IAvaloniaDiagnosticsDeadLetterConfirmationService>();
        _permissionService = services.GetService<IClientPermissionService>();
        _requeueDeadLetterCommand = new AsyncRelayCommand<DiagnosticsDeadLetterRow>(RequeueDeadLetterAsync, CanOperateDeadLetter);
        _deleteDeadLetterCommand = new AsyncRelayCommand<DiagnosticsDeadLetterRow>(DeleteDeadLetterAsync, CanOperateDeadLetter);
        FeedbackMessage = "诊断页已接入注册、启动、持久化和 Cloud/MES 死信运维状态。";
    }

    public ObservableCollection<RuntimeRegistrationRow> RuntimeRegistrations { get; } = [];

    public ObservableCollection<DiagnosticsModuleRegistrationRow> ModuleRegistrations { get; } = [];

    public ObservableCollection<DiagnosticsPersistenceRow> PersistenceRows { get; } = [];

    public ObservableCollection<DiagnosticsPluginStateRow> PluginStates { get; } = [];

    public ObservableCollection<DiagnosticsIssueRow> Issues { get; } = [];

    public ObservableCollection<DiagnosticsIoWriteGateRow> IoWriteGateRows { get; } = [];

    public ObservableCollection<DiagnosticsPlcWriteTraceRow> PlcWriteTraceRows { get; } = [];

    public ObservableCollection<DiagnosticsFieldAcceptanceSummaryRow> FieldAcceptanceRows { get; } = [];

    public ObservableCollection<DiagnosticsDeadLetterRow> CloudDeadLetters { get; } = [];

    public ObservableCollection<DiagnosticsDeadLetterRow> MesDeadLetters { get; } = [];

    [ObservableProperty]
    private string lastGeneratedText = "启动诊断尚未生成。";

    [ObservableProperty]
    private string configurationProfileText = "配置概况：-";

    [ObservableProperty]
    private string detailText = "等待刷新。";

    [ObservableProperty]
    private string feedbackMessage = string.Empty;

    public IAsyncRelayCommand RefreshCommand => _refreshCommand;

    public IAsyncRelayCommand<DiagnosticsDeadLetterRow> RequeueDeadLetterCommand => _requeueDeadLetterCommand;

    public IAsyncRelayCommand<DiagnosticsDeadLetterRow> DeleteDeadLetterCommand => _deleteDeadLetterCommand;

    public bool CanOperateDeadLetters => _permissionService?.IsLocalAdmin ?? false;

    public override async Task OnActivatedAsync()
    {
        StartDeadLetterPermissionObserving();
        await RefreshAsync();
    }

    public override Task OnDeactivatedAsync()
    {
        StopDeadLetterPermissionObserving();
        return Task.CompletedTask;
    }

    internal async Task RefreshAsync()
    {
        try
        {
            var report = ResolveOptional<IStartupDiagnosticsStore>()?.Current ?? StartupDiagnosticsReport.Empty();

            ApplyRuntimeRegistrations(ResolveOptional<IStationRuntimeRegistry>()?.GetRegistrations());
            ApplyModuleRegistrations(report.ModuleRegistrations);
            var syncDiagnostics = await ApplyPersistenceRowsAsync();
            if (syncDiagnostics is not null)
            {
                ApplyDeadLetterRows(syncDiagnostics);
            }

            ApplyPluginStates(report.PluginStates);
            ApplyIssues(report.Issues);
            ApplyIoWriteGateRows();
            ApplyPlcWriteTraceRows();
            ApplyFieldAcceptanceSummary(report, syncDiagnostics);

            ConfigurationProfileText = DiagnosticsReportProjectionFormatter.BuildConfigurationProfileText(report.ConfigurationProfile);
            LastGeneratedText = report.GeneratedAt == DateTime.MinValue
                ? "启动诊断尚未生成。"
                : $"最近生成：{report.GeneratedAt:yyyy-MM-dd HH:mm:ss}";
            DetailText = DiagnosticsReportProjectionFormatter.BuildDetailText(report, syncDiagnostics);
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
                DiagnosticsReportProjectionFormatter.FormatEnabled(item.IsEnabled),
                DiagnosticsReportProjectionFormatter.FormatRegistered(item.HasCellDataRegistration),
                DiagnosticsReportProjectionFormatter.FormatRegistered(item.HasRuntimeFactory),
                DiagnosticsReportProjectionFormatter.FormatRegistered(item.HasCloudUploader),
                DiagnosticsReportProjectionFormatter.FormatRegistered(item.HasMesUploader),
                DiagnosticsReportProjectionFormatter.FormatRegistered(item.HasHardwareProfile)))
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
        var cloudStatus = _statusClassifier.ClassifyCloud(snapshot.Cloud);
        var mesStatus = _statusClassifier.ClassifyMes(snapshot.Mes);

        var rows = new[]
        {
            new DiagnosticsPersistenceRow(
                "生产上下文持久化",
                context.CorruptFileCount == 0 ? "正常" : "异常",
                context.CorruptFileCount == 0
                    ? "未发现坏档。"
                    : $"坏档数量：{context.CorruptFileCount}；最近发现：{DiagnosticsReportProjectionFormatter.FormatTime(context.LastCorruptDetectedAt)}"),
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
                DiagnosticsReportProjectionFormatter.FormatTraceKind(item.Kind),
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
        var runtimeStatus = runtimeState is null ? "未接入" : DiagnosticsReportProjectionFormatter.FormatRuntimeModeStatus(runtimeState);
        var runtimeMessage = runtimeState is null
            ? "当前容器未注册 Avalonia 运行状态服务。"
            : DiagnosticsReportProjectionFormatter.BuildRuntimeModeMessage(runtimeState);

        var latestIo = ResolveOptional<IIoViewWriteGateAuditStore>()?.GetRecent(1).FirstOrDefault();
        var latestIoStatus = latestIo is null ? "暂无申请" : DiagnosticsReportProjectionFormatter.FormatIoWriteKind(latestIo.Kind);
        var latestIoMessage = latestIo is null
            ? "本次启动尚未发生 I/O 写入申请。"
            : $"时间：{DiagnosticsReportProjectionFormatter.FormatTime(latestIo.OccurredAt)}；设备：{latestIo.DeviceName}；业务组：{latestIo.BusinessGroup}；写入值：{latestIo.Value?.ToString() ?? "--"}；{latestIo.Message}";

        var latestTrace = ResolveOptional<IPlcIoWriteTraceStore>()?.GetRecent(1).FirstOrDefault();
        var latestTraceStatus = latestTrace is null ? "暂无轨迹" : DiagnosticsReportProjectionFormatter.FormatTraceKind(latestTrace.Kind);
        var latestTraceMessage = latestTrace is null
            ? "本次启动尚未记录 PLC 块写入轨迹。"
            : DiagnosticsReportProjectionFormatter.BuildPlcTraceSummary(latestTrace);

        var cloudStatus = diagnostics is null
            ? "未接入"
            : DiagnosticsReportProjectionFormatter.FormatCloudDiagnosticStatus(_statusClassifier.ClassifyCloud(diagnostics.Cloud));
        var cloudMessage = diagnostics is null
            ? "当前容器未注册同步诊断查询服务。"
            : DiagnosticsReportProjectionFormatter.BuildCloudReadonlySummary(diagnostics.Cloud);

        var mesStatus = diagnostics is null
            ? "未接入"
            : DiagnosticsReportProjectionFormatter.FormatMesDiagnosticStatus(_statusClassifier.ClassifyMes(diagnostics.Mes));
        var mesMessage = diagnostics is null
            ? "当前容器未注册同步诊断查询服务。"
            : DiagnosticsReportProjectionFormatter.BuildMesReadonlySummary(diagnostics.Mes);

        Replace(FieldAcceptanceRows,
        [
            new DiagnosticsFieldAcceptanceSummaryRow("运行模式", runtimeStatus, runtimeMessage),
            new DiagnosticsFieldAcceptanceSummaryRow(
                "启动诊断",
                report.GeneratedAt == DateTime.MinValue ? "未生成" : "已生成",
                $"模块 {report.ModuleRegistrations.Count} 个；PLC 设备 {report.DeviceBindings.Count} 个；阻断问题 {report.Issues.Count} 个；运行目录 {report.ConfigurationProfile.RuntimeDataRoot}。"),
            new DiagnosticsFieldAcceptanceSummaryRow(
                "运行目录证据",
                "只读路径",
                DiagnosticsReportProjectionFormatter.JoinSummary(
                    $"运行目录：{report.ConfigurationProfile.RuntimeDataRoot}",
                    runtimeState is null || string.IsNullOrWhiteSpace(runtimeState.DiagnosticsLogPath) ? null : $"诊断日志：{runtimeState.DiagnosticsLogPath}",
                    "本页只展示路径，不修改运行目录。")),
            new DiagnosticsFieldAcceptanceSummaryRow(
                "Cloud/MES 死信运维",
                CanOperateDeadLetters ? "本地管理员可操作" : "只读",
                "Cloud 与 MES 死信分开展示；本地管理员可重新入队或删除本地死信记录；操作只进入对应本地补偿链路，不直接调用上传接口。"),
            new DiagnosticsFieldAcceptanceSummaryRow("I/O 写入申请", latestIoStatus, latestIoMessage),
            new DiagnosticsFieldAcceptanceSummaryRow("PLC 块写入轨迹", latestTraceStatus, latestTraceMessage),
            new DiagnosticsFieldAcceptanceSummaryRow("Cloud 状态", cloudStatus, cloudMessage),
            new DiagnosticsFieldAcceptanceSummaryRow("MES 状态", mesStatus, mesMessage),
            new DiagnosticsFieldAcceptanceSummaryRow(
                "Cloud/MES 差异",
                "独立链路",
                "Cloud 与 MES 状态、死信和重新入队目标分开展示；本页不合并补偿链路，不提供强制上传入口。")
        ]);
    }

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
