using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;
using IIoT.Edge.UI.Avalonia.Localization;
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
