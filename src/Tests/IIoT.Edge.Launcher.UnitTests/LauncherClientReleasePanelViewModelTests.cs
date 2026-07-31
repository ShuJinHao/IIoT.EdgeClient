using System.Globalization;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Shared.Localization;
using Xunit;

namespace IIoT.Edge.Launcher.UnitTests;

public sealed class LauncherClientReleasePanelViewModelTests
{
    [Fact]
    public void Constructor_WhenUpdateGateIsMissing_ShouldFailClosed()
        => Assert.Throws<ArgumentNullException>(
            () => new LauncherClientReleasePanelViewModel(
                new RecordingReleaseService(CreateReleaseCatalog()),
                new LauncherUpdateTargetFactory(),
                new StubShellLaunchService(),
                null!));

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
    public async Task CheckAsync_WhenUnrelatedProfileCatalogFails_ShouldKeepOwnedPluginAvailable()
    {
        const string cpModuleId = "IIoT.Edge.CP";
        var apCatalog = CreateReleaseCatalog();
        var cpFallback = new EdgeReleaseCatalogResult(
            EdgeReleaseCatalogState.CatalogUnavailable,
            "stable",
            "win-x64",
            "1.0.0",
            "1.0.0",
            [
                ..apCatalog.Components,
                new EdgeComponentVersionPlan(
                    EdgeComponentKind.Plugin,
                    cpModuleId,
                    "CP",
                    "1.0.0",
                    [])
            ],
            "CP catalog unavailable");
        var service = new ProfileReleaseService(
            new Dictionary<string, EdgeReleaseCatalogResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["line-a"] = apCatalog,
                ["line-b"] = cpFallback
            });
        var panel = CreatePanel(service);
        var apProfile = Profile("line-a") with
        {
            ExpectedModuleIds = [ModuleId]
        };
        var cpProfile = Profile("line-b") with
        {
            ExpectedModuleIds = [cpModuleId]
        };

        await panel.CheckAsync([apProfile, cpProfile]);

        Assert.True(panel.Components.Single(component => component.ModuleId == ModuleId).IsCatalogAvailable);
        Assert.False(panel.Components.Single(component => component.ModuleId == cpModuleId).IsCatalogAvailable);
    }

    [Fact]
    public async Task CheckAsync_WhenCatalogPlanHasNoVersions_ShouldRemainUnavailable()
    {
        var catalog = CreateReleaseCatalog();
        var emptyHost = catalog.Components
            .Single(component => component.ComponentKind == EdgeComponentKind.Host) with
        {
            Versions = []
        };
        var panel = CreatePanel(new RecordingReleaseService(catalog with
        {
            Components = [emptyHost]
        }));

        await panel.CheckAsync(Profile());

        var host = Assert.Single(panel.Components);
        Assert.False(host.IsCatalogAvailable);
        Assert.Empty(host.Versions);
    }

    [Fact]
    public async Task CheckAsync_WhenRefreshOverlaps_ShouldUseSingleCatalogRequest()
    {
        var service = new BlockingCheckReleaseService(CreateReleaseCatalog());
        var panel = CreatePanel(service);

        var first = panel.CheckAsync(Profile());
        await service.WaitForCheckStartAsync();
        var overlapping = panel.CheckAsync(Profile());

        Assert.True(overlapping.IsCompletedSuccessfully);
        Assert.Equal(1, service.CheckCallCount);

        service.Complete();
        await first;
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
    public async Task ApplyVersionAsync_WhenPluginInstallSucceeds_ShouldRefreshCatalogBeforeCompleting()
    {
        var initialCatalog = CreateReleaseCatalog();
        var installedCatalog = initialCatalog with
        {
            Components = initialCatalog.Components
                .Select(component => component.ModuleId == ModuleId
                    ? component with
                    {
                        CurrentVersion = "1.1.0",
                        Versions = component.Versions
                            .Select(option => option.Version == "1.1.0"
                                ? option with
                                {
                                    Status = EdgeVersionStatus.Current,
                                    CanApply = false
                                }
                                : option)
                            .ToArray()
                    }
                    : component)
                .ToArray()
        };
        var service = new RecordingReleaseService(initialCatalog, installedCatalog);
        var panel = CreatePanel(service);
        await panel.CheckAsync(Profile());
        var newer = panel.Components
            .Single(component => component.ModuleId == ModuleId)
            .Versions.Single(option => option.Version == "1.1.0");

        await panel.ApplyVersionAsync(newer);

        var refreshedPlugin = panel.Components.Single(component => component.ModuleId == ModuleId);
        var installedVersion = refreshedPlugin.Versions.Single(option => option.Version == "1.1.0");
        Assert.Equal(2, service.CheckCallCount);
        Assert.Equal("1.1.0", refreshedPlugin.CurrentVersion);
        Assert.Equal(EdgeVersionStatus.Current, installedVersion.Status);
        Assert.False(installedVersion.CanApply);
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
    public async Task ApplyVersionAsync_WhenHostNeedsPluginComposition_ShouldConfirmAndApplyComposition()
    {
        var requiredComposition = new EdgeVersionSelection(
            "1.1.0",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ModuleId] = "1.1.0"
            });
        var catalog = CreateReleaseCatalog();
        var host = catalog.Components.Single(component => component.ComponentKind == EdgeComponentKind.Host);
        var composedHost = host with
        {
            Versions = host.Versions
                .Select(option => option.Version == "1.1.0"
                    ? option with
                    {
                        CompatibilityIssue = $"需要 {ModuleId} 1.1.0。",
                        RequiredComposition = requiredComposition
                    }
                    : option)
                .ToArray()
        };
        var service = new RecordingReleaseService(catalog with
        {
            Components = catalog.Components
                .Select(component => component.ComponentKind == EdgeComponentKind.Host ? composedHost : component)
                .ToArray()
        });
        var panel = CreatePanel(service);
        LauncherVersionChangeConfirmationRequest? confirmation = null;
        panel.ConfirmVersionChangeAsync = request =>
        {
            confirmation = request;
            return Task.FromResult(true);
        };
        await panel.CheckAsync(Profile());
        var hostVersion = panel.Components
            .Single(component => component.ComponentKind == EdgeComponentKind.Host)
            .Versions.Single(option => option.Version == "1.1.0");

        await panel.ApplyVersionAsync(hostVersion);

        Assert.NotNull(confirmation);
        Assert.Equal("1.1.0", confirmation!.RequiredPluginVersions![ModuleId]);
        Assert.NotNull(service.AppliedComposition);
        Assert.Equal(requiredComposition.HostVersion, service.AppliedComposition!.HostVersion);
        Assert.Equal("1.1.0", service.AppliedComposition.PluginVersions[ModuleId]);
        Assert.Null(service.AppliedHostVersion);
    }

    [Fact]
    public async Task ApplyVersionAsync_WhenHostCompositionCoversMultipleProfiles_ShouldPassEveryTarget()
    {
        var requiredComposition = new EdgeVersionSelection(
            "1.1.0",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ModuleId] = "1.1.0"
            });
        var catalog = CreateReleaseCatalog();
        var host = catalog.Components.Single(component => component.ComponentKind == EdgeComponentKind.Host);
        var service = new RecordingReleaseService(catalog with
        {
            Components = catalog.Components
                .Select(component => component.ComponentKind == EdgeComponentKind.Host
                    ? host with
                    {
                        Versions = host.Versions.Select(option => option.Version == "1.1.0"
                                ? option with { RequiredComposition = requiredComposition }
                                : option)
                            .ToArray()
                    }
                    : component)
                .ToArray()
        });
        var panel = CreatePanel(service);
        panel.ConfirmVersionChangeAsync = _ => Task.FromResult(true);
        await panel.CheckAsync([Profile("line-a"), Profile("line-b")]);
        var hostVersion = panel.Components
            .Single(component => component.ComponentKind == EdgeComponentKind.Host)
            .Versions.Single(option => option.Version == "1.1.0");

        await panel.ApplyVersionAsync(hostVersion);

        Assert.NotNull(service.AppliedCompositionTargets);
        Assert.Equal(2, service.AppliedCompositionTargets!.Count);
        Assert.Equal(["line-a", "line-b"], service.AppliedCompositionTargets.Select(target => target.MachineProfile));
        Assert.Equal("1.1.0", service.AppliedComposition!.PluginVersions[ModuleId]);
        Assert.Null(service.AppliedHostVersion);
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
    public async Task ApplyVersionAsync_WhenAnotherLauncherOwnsGate_ShouldNotStartSecondInstall()
    {
        var sharedGate = new TrackingUpdateOperationGate();
        using var firstLauncherLease = sharedGate.TryAcquire();
        Assert.NotNull(firstLauncherLease);
        var service = new RecordingReleaseService(CreateReleaseCatalog());
        var panel = CreatePanel(
            service,
            updateOperationGate: sharedGate);
        await panel.CheckAsync(Profile());
        var newer = panel.Components
            .Single(component => component.ModuleId == ModuleId)
            .Versions.Single(option => option.Version == "1.1.0");

        await panel.ApplyVersionAsync(newer);

        Assert.Null(service.AppliedPluginModuleId);
        Assert.Contains(
            "Launcher_ClientRelease_StatusBusy",
            panel.StatusMessage,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyVersionAsync_WhenInstallThrows_ShouldReleaseGate(
        bool cancellation)
    {
        var gate = new TrackingUpdateOperationGate();
        var service = new ThrowingApplyReleaseService(
            CreateReleaseCatalog(),
            cancellation);
        var panel = CreatePanel(
            service,
            updateOperationGate: gate);
        await panel.CheckAsync(Profile());
        var newer = panel.Components
            .Single(component => component.ModuleId == ModuleId)
            .Versions.Single(option => option.Version == "1.1.0");

        if (cancellation)
        {
            await panel.ApplyVersionAsync(newer);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => panel.ApplyVersionAsync(newer));
        }

        Assert.False(gate.IsHeld);
        Assert.Equal(1, gate.ReleaseCount);
        using var nextLease = gate.TryAcquire();
        Assert.NotNull(nextLease);
        Assert.Equal(1, service.ApplyCallCount);
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

    private const string ModuleId = "IIoT.Edge.TestPlugin";

    private static LauncherClientReleasePanelViewModel CreatePanel(
        IEdgeReleaseService releaseService,
        IShellLaunchService? shellLaunchService = null,
        IAppLanguageService? languageService = null,
        ILauncherUpdateOperationGate? updateOperationGate = null)
        => new(
            releaseService,
            new LauncherUpdateTargetFactory(),
            shellLaunchService ?? new StubShellLaunchService(),
            updateOperationGate ?? new TrackingUpdateOperationGate(),
            languageService);

    private static LauncherProfileDefinition Profile(string profileId = "testplugin")
        => new(profileId, "测试插件", "测试工序", null, profileId, "IIoT.Edge.Shell", "Shell", "#000000");

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
                    "测试插件",
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
            "测试插件",
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
        public bool HasAnyRunningShellProcess() => hasRunningShellProcess;

        public bool IsProfileRunning(LauncherProfileDefinition profile) => false;

        public Task<ShellLaunchResult> LaunchAsync(
            LauncherProfileDefinition profile,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ShellLaunchResult(false, []));
    }

    private sealed class TrackingUpdateOperationGate : ILauncherUpdateOperationGate
    {
        public bool IsHeld { get; private set; }

        public int ReleaseCount { get; private set; }

        public IDisposable? TryAcquire()
        {
            if (IsHeld)
            {
                return null;
            }

            IsHeld = true;
            return new Lease(this);
        }

        public IDisposable? TryAcquireUpdate() => TryAcquire();

        public string CreateShellLaunchReadyPath()
            => Path.Combine(Path.GetTempPath(), $"launcher-panel-{Guid.NewGuid():N}.json");

        private sealed class Lease(TrackingUpdateOperationGate owner) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                owner.IsHeld = false;
                owner.ReleaseCount++;
            }
        }
    }

    private sealed class BlockingCheckReleaseService(
        EdgeReleaseCatalogResult result) : IEdgeReleaseService
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _complete = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int CheckCallCount { get; private set; }

        public async Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
        {
            CheckCallCount++;
            _started.TrySetResult();
            await _complete.Task.WaitAsync(cancellationToken);
            return result;
        }

        public Task<EdgePluginInstallResult> ApplyPluginVersionAsync(
            EdgeUpdateTarget target,
            string moduleId,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not used"));

        public Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
            EdgeUpdateTarget target,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(false, "not used"));

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            EdgeUpdateTarget target,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not used"));

        public Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeVersionReportResult.Failed("not used"));

        public Task WaitForCheckStartAsync()
            => _started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        public void Complete() => _complete.TrySetResult();
    }

    private sealed class RecordingReleaseService(
        EdgeReleaseCatalogResult checkResult,
        EdgeReleaseCatalogResult? afterApplyCheckResult = null) : IEdgeReleaseService
    {
        public int CheckCallCount { get; private set; }

        public string? AppliedPluginModuleId { get; private set; }

        public string? AppliedPluginVersion { get; private set; }

        public string? AppliedHostVersion { get; private set; }

        public EdgeVersionSelection? AppliedComposition { get; private set; }

        public IReadOnlyList<EdgeUpdateTarget>? AppliedCompositionTargets { get; private set; }

        public Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
        {
            CheckCallCount++;
            return Task.FromResult(
                AppliedPluginModuleId is not null && afterApplyCheckResult is not null
                    ? afterApplyCheckResult
                    : checkResult);
        }

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
        {
            AppliedComposition = selection;
            AppliedCompositionTargets = [target];
            progress?.Report(100);
            return Task.FromResult(EdgePluginInstallResult.Succeeded(selection.PluginVersions.Keys.ToArray()));
        }

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            IReadOnlyList<EdgeUpdateTarget> targets,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            AppliedComposition = selection;
            AppliedCompositionTargets = targets;
            progress?.Report(100);
            return Task.FromResult(EdgePluginInstallResult.Succeeded(selection.PluginVersions.Keys.ToArray()));
        }

        public Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeVersionReportResult.Succeeded());
    }

    private sealed class ProfileReleaseService(
        IReadOnlyDictionary<string, EdgeReleaseCatalogResult> checksByMachineProfile)
        : IEdgeReleaseService
    {
        public Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(checksByMachineProfile[target.MachineProfile]);

        public Task<EdgePluginInstallResult> ApplyPluginVersionAsync(
            EdgeUpdateTarget target,
            string moduleId,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
            EdgeUpdateTarget target,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            EdgeUpdateTarget target,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            IReadOnlyList<EdgeUpdateTarget> targets,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeVersionReportResult.Succeeded());
    }

    private sealed class ThrowingApplyReleaseService(
        EdgeReleaseCatalogResult checkResult,
        bool cancellation) : IEdgeReleaseService
    {
        public int ApplyCallCount { get; private set; }

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
            ApplyCallCount++;
            return cancellation
                ? Task.FromException<EdgePluginInstallResult>(
                    new OperationCanceledException())
                : Task.FromException<EdgePluginInstallResult>(
                    new InvalidOperationException("simulated install failure"));
        }

        public Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
            EdgeUpdateTarget target,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            EdgeUpdateTarget target,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            IReadOnlyList<EdgeUpdateTarget> targets,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

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
