using System.Globalization;
using IIoT.Edge.Application.Abstractions.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Shared.Localization;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class LauncherClientReleasePanelViewModelTests
{
    [Fact]
    public async Task CheckAsync_ShouldExposeAllComponentVersions()
    {
        var panel = CreatePanel(new RecordingReleaseService(CreateReleaseCatalog()));

        await panel.CheckAsync(Profile());

        var host = Assert.Single(panel.Components, component => component.ComponentKind == EdgeComponentKind.Host);
        var plugin = Assert.Single(panel.Components, component => component.ModuleId == ModuleId);
        Assert.Equal(2, host.Versions.Count);
        Assert.Equal(4, plugin.Versions.Count);
        Assert.Contains(plugin.Versions, option => option.Version == "1.1.0" && option.Status == EdgeVersionStatus.Newer);
        Assert.Contains(plugin.Versions, option => option.Version == "1.0.0" && option.Status == EdgeVersionStatus.Current);
        Assert.Contains(plugin.Versions, option => option.Version == "0.9.0" && option.Status == EdgeVersionStatus.Older);
        Assert.Contains(plugin.Versions, option => option.Version == "0.8.0" && option.Status == EdgeVersionStatus.Deprecated);
    }

    [Fact]
    public async Task ApplyVersionAsync_WhenPluginOlderVersionConfirmed_ShouldCallApplyPluginVersion()
    {
        var service = new RecordingReleaseService(CreateReleaseCatalog());
        var panel = CreatePanel(service);
        var confirmCount = 0;
        panel.ConfirmVersionChangeAsync = _ =>
        {
            confirmCount++;
            return Task.FromResult(true);
        };
        await panel.CheckAsync(Profile());
        var older = panel.Components
            .Single(component => component.ModuleId == ModuleId)
            .Versions.Single(option => option.Version == "0.9.0");

        await panel.ApplyVersionAsync(older);

        Assert.Equal(1, confirmCount);
        Assert.Equal(ModuleId, service.AppliedPluginModuleId);
        Assert.Equal("0.9.0", service.AppliedPluginVersion);
    }

    [Fact]
    public async Task ApplyVersionAsync_WhenPluginOlderVersionCancelled_ShouldNotCallApplyPluginVersion()
    {
        var service = new RecordingReleaseService(CreateReleaseCatalog());
        var panel = CreatePanel(service);
        panel.ConfirmVersionChangeAsync = _ => Task.FromResult(false);
        await panel.CheckAsync(Profile());
        var older = panel.Components
            .Single(component => component.ModuleId == ModuleId)
            .Versions.Single(option => option.Version == "0.9.0");

        await panel.ApplyVersionAsync(older);

        Assert.Null(service.AppliedPluginModuleId);
        Assert.Null(service.AppliedPluginVersion);
    }

    [Fact]
    public async Task ApplyVersionAsync_WhenHostVersionSelected_ShouldCallApplyHostVersion()
    {
        var service = new RecordingReleaseService(CreateReleaseCatalog());
        var panel = CreatePanel(service);
        await panel.CheckAsync(Profile());
        var hostVersion = panel.Components
            .Single(component => component.ComponentKind == EdgeComponentKind.Host)
            .Versions.Single(option => option.Version == "1.1.0");

        await panel.ApplyVersionAsync(hostVersion);

        Assert.Equal("1.1.0", service.AppliedHostVersion);
    }

    [Fact]
    public async Task ApplyVersionAsync_WhenShellRunning_ShouldNotApplyPluginVersion()
    {
        var service = new RecordingReleaseService(CreateReleaseCatalog());
        var panel = CreatePanel(service, new StubShellLaunchService(hasRunningShellProcess: true));
        await panel.CheckAsync(Profile());
        var newer = panel.Components
            .Single(component => component.ModuleId == ModuleId)
            .Versions.Single(option => option.Version == "1.1.0");

        await panel.ApplyVersionAsync(newer);

        Assert.Null(service.AppliedPluginModuleId);
        Assert.Null(service.AppliedPluginVersion);
    }

    [Fact]
    public async Task LanguageChanged_ShouldRefreshVersionOptionTexts()
    {
        var languageService = new TestLanguageService();
        var panel = CreatePanel(new RecordingReleaseService(CreateReleaseCatalog()), languageService: languageService);
        await panel.CheckAsync(Profile());
        var component = panel.Components.Single(item => item.ModuleId == ModuleId);
        var older = component.Versions.Single(option => option.Version == "0.9.0");

        Assert.Equal("回退", older.ActionText);
        Assert.Equal("展开版本", component.ExpandActionText);

        languageService.Change(CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("Rollback", older.ActionText);
        Assert.Equal("Show versions", component.ExpandActionText);
    }

    private const string ModuleId = "IIoT.Edge.Module.Homogenization";

    private static LauncherClientReleasePanelViewModel CreatePanel(
        IEdgeReleaseService releaseService,
        IShellLaunchService? shellLaunchService = null,
        IAppLanguageService? languageService = null)
        => new(
            releaseService,
            new LauncherUpdateTargetFactory(),
            shellLaunchService ?? new StubShellLaunchService(),
            languageService);

    private static LauncherProfileDefinition Profile()
        => new("homogenization", "均浆", "测试工序", null, "homogenization", "IIoT.Edge.Shell", "Shell", "#000000");

    private static EdgeReleaseCatalogResult CreateReleaseCatalog()
        => new(
            EdgeReleaseCatalogState.Succeeded,
            "stable",
            "win-x64",
            "1.0.0",
            "1.0.0",
            [
                new EdgeComponentVersionPlan(
                    EdgeComponentKind.Host,
                    "Host",
                    "Edge Host",
                    "1.0.0",
                    [
                        new EdgeVersionOption(
                            "1.1.0",
                            EdgeVersionStatus.Newer,
                            true,
                            null,
                            HostRelease: new EdgeHostVersionRelease(CreateHostEntry("1.1.0", "Host update"))),
                        new EdgeVersionOption(
                            "1.0.0",
                            EdgeVersionStatus.Current,
                            false,
                            null,
                            HostRelease: new EdgeHostVersionRelease(CreateHostEntry("1.0.0", "Current host")))
                    ]),
                new EdgeComponentVersionPlan(
                    EdgeComponentKind.Plugin,
                    ModuleId,
                    "均浆",
                    "1.0.0",
                    [
                        new EdgeVersionOption(
                            "1.1.0",
                            EdgeVersionStatus.Newer,
                            true,
                            null,
                            PluginRelease: CreatePluginRelease("1.1.0", "Plugin update")),
                        new EdgeVersionOption(
                            "1.0.0",
                            EdgeVersionStatus.Current,
                            false,
                            null,
                            PluginRelease: CreatePluginRelease("1.0.0", "Current plugin")),
                        new EdgeVersionOption(
                            "0.9.0",
                            EdgeVersionStatus.Older,
                            true,
                            null,
                            PluginRelease: CreatePluginRelease("0.9.0", "Rollback plugin")),
                        new EdgeVersionOption(
                            "0.8.0",
                            EdgeVersionStatus.Deprecated,
                            true,
                            null,
                            PluginRelease: CreatePluginRelease("0.8.0", "Deprecated plugin"))
                    ])
            ]);

    private static EdgeHostVersionEntry CreateHostEntry(string version, string releaseNotes)
        => new(
            Guid.NewGuid(),
            "stable",
            version,
            "1.0.0",
            "win-x64",
            "net10.0",
            $"https://example.invalid/host-{version}.nupkg",
            "sha256",
            2048,
            releaseNotes,
            "Published",
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow);

    private static EdgePluginVersionRelease CreatePluginRelease(string version, string releaseNotes)
        => new(
            ModuleId,
            "均浆",
            null,
            null,
            null,
            new EdgePluginVersionEntry(
                Guid.NewGuid(),
                "stable",
                version,
                "1.0.0",
                "1.0.0",
                "99.0.0",
                "win-x64",
                "net10.0",
                $"https://example.invalid/plugin-{version}.zip",
                "sha256",
                1024,
                releaseNotes,
                [],
                "Published",
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow));

    private sealed class StubShellLaunchService(bool hasRunningShellProcess = false) : IShellLaunchService
    {
        public bool HasRunningShellProcess { get; } = hasRunningShellProcess;

        public void Launch(LauncherProfileDefinition profile)
        {
        }
    }

    private sealed class RecordingReleaseService(EdgeReleaseCatalogResult checkResult) : IEdgeReleaseService
    {
        public string? AppliedPluginModuleId { get; private set; }

        public string? AppliedPluginVersion { get; private set; }

        public string? AppliedHostVersion { get; private set; }

        public Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(checkResult);

        public Task<EdgePluginInstallResult> ApplyPluginVersionAsync(
            EdgeUpdateTarget target,
            string moduleId,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            AppliedPluginModuleId = moduleId;
            AppliedPluginVersion = version;
            progress?.Report(100);
            return Task.FromResult(EdgePluginInstallResult.Succeeded([moduleId]));
        }

        public Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
            EdgeUpdateTarget target,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            AppliedHostVersion = version;
            progress?.Report(100);
            return Task.FromResult(new EdgeHostUpdateApplyResult(true));
        }

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            EdgeUpdateTarget target,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not used"));

        public Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeVersionReportResult.Succeeded());
    }

    private sealed class TestLanguageService : IAppLanguageService
    {
        private static readonly LanguageOption Zh = new(CultureInfo.GetCultureInfo("zh-CN"), "中文");
        private static readonly LanguageOption En = new(CultureInfo.GetCultureInfo("en-US"), "English");

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Texts =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["zh-CN"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Launcher_VersionManagement_ButtonRollback"] = "回退",
                    ["Launcher_VersionManagement_ButtonExpand"] = "展开版本",
                    ["Launcher_VersionManagement_ButtonCollapse"] = "收起版本",
                    ["Launcher_VersionManagement_StatusOlder"] = "可回退",
                    ["Launcher_VersionManagement_StatusNewer"] = "可更新",
                    ["Launcher_VersionManagement_StatusCurrent"] = "当前版本",
                    ["Launcher_VersionManagement_StatusDeprecated"] = "已弃用",
                    ["Launcher_VersionManagement_ComponentHost"] = "宿主",
                    ["Launcher_VersionManagement_ComponentPlugin"] = "插件"
                },
                ["en-US"] = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Launcher_VersionManagement_ButtonRollback"] = "Rollback",
                    ["Launcher_VersionManagement_ButtonExpand"] = "Show versions",
                    ["Launcher_VersionManagement_ButtonCollapse"] = "Hide versions",
                    ["Launcher_VersionManagement_StatusOlder"] = "Rollback available",
                    ["Launcher_VersionManagement_StatusNewer"] = "Newer",
                    ["Launcher_VersionManagement_StatusCurrent"] = "Current",
                    ["Launcher_VersionManagement_StatusDeprecated"] = "Deprecated",
                    ["Launcher_VersionManagement_ComponentHost"] = "Host",
                    ["Launcher_VersionManagement_ComponentPlugin"] = "Plugin"
                }
            };

        public CultureInfo Current { get; private set; } = Zh.Culture;

        public LanguageOption CurrentOption => SupportedLanguages.Single(option => option.Culture.Equals(Current));

        public IReadOnlyList<LanguageOption> SupportedLanguages { get; } = [Zh, En];

        public event EventHandler? LanguageChanged;

        public void Initialize()
        {
        }

        public void Change(CultureInfo culture)
        {
            Current = culture;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key, string fallback = "")
            => Texts.TryGetValue(Current.Name, out var languageTexts) &&
               languageTexts.TryGetValue(key, out var text)
                ? text
                : fallback;

        public string Format(string key, string fallback, params object[] args)
            => string.Format(Current, GetString(key, fallback), args);
    }
}
