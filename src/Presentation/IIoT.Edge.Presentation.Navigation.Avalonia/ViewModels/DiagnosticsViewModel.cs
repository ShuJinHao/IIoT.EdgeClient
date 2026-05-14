using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Diagnostics;
using IIoT.Edge.Application.Modules.Diagnostics;
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
        FeedbackMessage = "诊断页仅读取当前注册与持久化状态，不执行清理或重试。";
    }

    public ObservableCollection<RuntimeRegistrationRow> RuntimeRegistrations { get; } = [];

    public ObservableCollection<DiagnosticsModuleRegistrationRow> ModuleRegistrations { get; } = [];

    public ObservableCollection<DiagnosticsPersistenceRow> PersistenceRows { get; } = [];

    public ObservableCollection<DiagnosticsPluginStateRow> PluginStates { get; } = [];

    public ObservableCollection<DiagnosticsIssueRow> Issues { get; } = [];

    [ObservableProperty]
    private string lastGeneratedText = "启动诊断尚未生成。";

    [ObservableProperty]
    private string configurationProfileText = "配置概况：--";

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
            await ApplyPersistenceRowsAsync();
            ApplyPluginStates(report.PluginStates);
            ApplyIssues(report.Issues);

            ConfigurationProfileText = BuildConfigurationProfileText(report.ConfigurationProfile);
            LastGeneratedText = report.GeneratedAt == DateTime.MinValue
                ? "启动诊断尚未生成。"
                : $"最近生成：{report.GeneratedAt:yyyy-MM-dd HH:mm:ss}";
            FeedbackMessage = $"已读取 {RuntimeRegistrations.Count} 个运行时注册、{ModuleRegistrations.Count} 个模块注册。";
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

    private async Task ApplyPersistenceRowsAsync()
    {
        var syncDiagnosticsQuery = ResolveOptional<IEdgeSyncDiagnosticsQuery>();
        if (syncDiagnosticsQuery is null)
        {
            Replace(PersistenceRows, [new DiagnosticsPersistenceRow("同步诊断", "未接入", "当前容器未注册同步诊断查询服务。")]);
            return;
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

    private static string BuildConfigurationProfileText(ConfigurationProfileSnapshot profile)
    {
        var machine = string.IsNullOrWhiteSpace(profile.MachineProfile)
            ? "未配置"
            : profile.MachineProfile;
        var loaded = profile.IsMachineProfileLoaded ? "已加载" : "未加载";
        return $"环境：{profile.EnvironmentName}；机型：{machine}（{loaded}）；运行目录：{profile.RuntimeDataRoot}";
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
