using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using Xunit;

namespace IIoT.Edge.Launcher.UiTests;

public sealed class LauncherWindowHeadlessTests
{
    [AvaloniaFact]
    public void MainWindow_WhenUnauthenticated_ShouldLoadLoginView()
    {
        var window = CreateMainWindow(CreateViewModel());

        try
        {
            window.Show();

            Assert.True(window.FindControl<Control>("LoginPageRoot")?.IsVisible);
            Assert.False(window.FindControl<Control>("SelectionPageRoot")?.IsVisible);
            Assert.NotNull(window.FindControl<TextBox>("PasswordInput"));
            Assert.Null(window.FindControl<TextBox>("NewPasswordInput"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_WhenLocalAccountSetupRequired_ShouldLoadSetupView()
    {
        var viewModel = CreateViewModel(LauncherAccountCatalogStatus.Missing);
        var window = CreateMainWindow(viewModel);

        try
        {
            window.Show();

            Assert.True(window.FindControl<Control>("LoginPageRoot")?.IsVisible);
            Assert.False(window.FindControl<Control>("LoginFormPanel")?.IsVisible);
            Assert.True(window.FindControl<Control>("AccountSetupPanel")?.IsVisible);
            Assert.NotNull(window.FindControl<Control>("InitializeAccountButton"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_WhenLocalAccountSetupRequiredAtMinimumSize_ShouldKeepActionAboveNotice()
    {
        var viewModel = CreateViewModel(LauncherAccountCatalogStatus.Missing);
        var window = CreateMainWindow(viewModel);
        window.Width = 1100;
        window.Height = 700;

        try
        {
            window.Show();
            window.UpdateLayout();

            var initializeButton = window.FindControl<Control>("InitializeAccountButton");
            var boundaryNotice = window.FindControl<Control>("LoginBoundaryNotice");
            var fieldsScrollHost = window.FindControl<EdgeScrollHost>("AccountSetupFieldsScrollHost");
            Assert.NotNull(initializeButton);
            Assert.NotNull(boundaryNotice);
            Assert.NotNull(fieldsScrollHost);

            var buttonOrigin = initializeButton.TranslatePoint(default, window);
            var noticeOrigin = boundaryNotice.TranslatePoint(default, window);
            Assert.NotNull(buttonOrigin);
            Assert.NotNull(noticeOrigin);
            Assert.True(initializeButton.Bounds.Height >= 44);
            Assert.True(fieldsScrollHost.Bounds.Height > 0);
            Assert.True(
                buttonOrigin.Value.Y + initializeButton.Bounds.Height + 12 <= noticeOrigin.Value.Y,
                "初始化按钮必须完整停留在说明条上方，不能再被说明遮挡。");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenAuthenticated_ShouldLoadSelectionView()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoginAsync("operator", "secret");
        var window = CreateMainWindow(viewModel);

        try
        {
            window.Show();

            Assert.False(window.FindControl<Control>("LoginPageRoot")?.IsVisible);
            Assert.True(window.FindControl<Control>("SelectionPageRoot")?.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenStartupHasLocalDiagnostics_ShouldShowSelectionNotice()
    {
        var diagnostics = new LauncherStartupDiagnosticStore();
        diagnostics.ReplaceArea(
            LauncherStartupDiagnosticAreas.EnabledPluginSelection,
            [
                new LauncherStartupDiagnostic(
                    LauncherStartupDiagnosticAreas.EnabledPluginSelection,
                    "LAUNCHER_PLUGIN_SELECTION_INVALID",
                    LauncherStartupDiagnosticRepairTargets.PluginSelection)
            ]);
        var viewModel = CreateViewModel(startupDiagnosticReader: diagnostics);
        await viewModel.LoginAsync("operator", "secret");
        var window = CreateMainWindow(viewModel);

        try
        {
            window.Show();
            window.UpdateLayout();

            Assert.True(window.FindControl<Control>("SelectionStartupDiagnosticsNotice")?.IsVisible);
            Assert.Contains(
                window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(static text => text.IsVisible)
                    .Select(static text => text.Text),
                text => text?.Contains("LAUNCHER_PLUGIN_SELECTION_INVALID", StringComparison.Ordinal) == true);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenAuthenticated_ShouldExposeDedicatedUpdateCenter()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoginAsync("operator", "secret");
        var window = CreateMainWindow(viewModel);

        try
        {
            window.Show();

            Assert.True(window.FindControl<Control>("UpdateCenterPanelRoot")?.IsVisible);
            Assert.NotNull(window.FindControl<Control>("RefreshUpdateCenterButton"));
            Assert.Null(window.FindControl<Control>("VersionHistoryButton"));
            var rowsGrid = window.FindControl<EdgeDataGrid>("UpdateCenterRowsGrid");
            Assert.NotNull(rowsGrid);
            Assert.Null(window.FindControl<Control>("ClientReleasePanelRoot"));
            Assert.NotNull(window.FindControl<EdgeProgressBar>("ClientReleaseProgressBar"));
            Assert.Equal(150d, rowsGrid.ViewportMaxHeight);
            Assert.NotEmpty(rowsGrid.Columns.OfType<EdgeActionColumn>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MainWindow_WhenAuthenticated_ShouldRenderOperatorTextWithoutInternalIdentifiers()
    {
        var viewModel = CreateViewModel();
        await viewModel.LoginAsync("operator", "secret");
        var window = CreateMainWindow(viewModel);

        try
        {
            window.Show();
            window.UpdateLayout();

            var visibleText = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(static textBlock => textBlock.IsVisible)
                .Select(static textBlock => textBlock.Text)
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .ToArray();

            Assert.Contains(viewModel.WelcomeText, visibleText);
            Assert.Contains(viewModel.ProfileSummaryText, visibleText);
            Assert.DoesNotContain("TestPlugin", visibleText);
            Assert.DoesNotContain("MachineProfile", visibleText);
            Assert.DoesNotContain("叠片", visibleText);
            Assert.DoesNotContain("测试插件", visibleText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ChangePasswordWindow_ShouldLoadDialog()
    {
        var window = new ChangePasswordWindow(CreateViewModel(), "operator")
        {
            Width = 640,
            Height = 520
        };

        try
        {
            window.Show();

            Assert.True(window.FindControl<Control>("OldPasswordInput")?.IsVisible);
            Assert.True(window.FindControl<Control>("ConfirmButton")?.IsVisible);
            Assert.Equal('●', window.FindControl<TextBox>("OldPasswordInput")?.PasswordChar);
            Assert.Equal('●', window.FindControl<TextBox>("NewPasswordInput")?.PasswordChar);
            Assert.Equal('●', window.FindControl<TextBox>("ConfirmPasswordInput")?.PasswordChar);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void VersionHistoryWindow_ShouldLoadDialog()
    {
        var viewModel = CreateViewModel();
        var component = new LauncherVersionComponentItem(
            EdgeComponentKind.Host,
            "Host",
            "Edge Host",
            "1.0.0",
            "宿主",
            "查看版本",
            []);
        var window = new VersionHistoryWindow(component, viewModel.ClientReleasePanel)
        {
            Width = 860,
            Height = 540
        };

        try
        {
            window.Show();

            Assert.True(window.FindControl<Control>("VersionHistoryWindowRoot")?.IsVisible);
            var rowsGrid = window.FindControl<EdgeDataGrid>("VersionHistoryRowsGrid");
            Assert.NotNull(rowsGrid);
            Assert.NotNull(window.FindControl<EdgeProgressBar>("VersionHistoryProgressBar"));
            Assert.Equal(0d, rowsGrid.ViewportMaxHeight);
            Assert.NotEmpty(rowsGrid.Columns.OfType<EdgeActionColumn>());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ReleaseNotesWindow_ShouldLoadDialog()
    {
        var detail = LauncherReleaseNotesDetailViewModel.FromVersionOption(
            new LauncherVersionOptionItem(
                EdgeComponentKind.Plugin,
                "TestPlugin",
                "测试插件",
                "1.0.0",
                "1.1.0",
                EdgeVersionStatus.Newer,
                canApply: true,
                compatibilityIssue: string.Empty,
                packageSizeText: "101.0 KB",
                publishedAtUtc: new DateTime(2026, 6, 22, 16, 15, 54, DateTimeKind.Utc),
                releaseNotes: "客户端更新：设备安装状态上报本机 IP/远端 IP、宿主和插件版本。",
                statusKind: "Warning",
                statusText: "可更新",
                actionKind: "Secondary",
                actionText: "更新"),
            "工序插件");
        var window = new ReleaseNotesWindow(detail)
        {
            Width = 640,
            Height = 520
        };

        try
        {
            window.Show();

            Assert.True(window.FindControl<Control>("ReleaseNotesWindowRoot")?.IsVisible);
            Assert.Equal(
                detail.ReleaseNotesText,
                window.FindControl<TextBlock>("ReleaseNotesTextBlock")?.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_ShouldNotExposeSeparateUpdateControls()
    {
        var window = CreateMainWindow(CreateViewModel());

        try
        {
            window.Show();

            Assert.Null(window.FindControl<Control>("CheckUpdatesButton"));
            Assert.Null(window.FindControl<Control>("ApplyUpdateButton"));
            Assert.Null(window.FindControl<Control>("UpdateProgressBar"));
        }
        finally
        {
            window.Close();
        }
    }

    private static MainWindow CreateMainWindow(LauncherMainViewModel viewModel)
        => new(
            viewModel,
            new LauncherLanguageService(Path.Combine(
                Path.GetTempPath(),
                $"iiot-launcher-test-language-{Guid.NewGuid():N}.json")))
        {
            Width = 1180,
            Height = 720
        };

    private static LauncherMainViewModel CreateViewModel(
        LauncherAccountCatalogStatus accountCatalogStatus = LauncherAccountCatalogStatus.Ready,
        ILauncherStartupDiagnosticReader? startupDiagnosticReader = null)
        => new(
            new StubLauncherProfileCatalog(
            [
                new LauncherProfileDefinition(
                    "shell",
                    "Shell",
                    "测试工序",
                    "测试客户",
                    "default",
                    "IIoT.Edge.Shell",
                    "Shell",
                    "#000000")
            ]),
            new StubLocalAccountAuthService(LauncherAuthenticationResult.Passed(
                new LauncherAccountRecord("operator", "operator", "hash", true)),
                accountCatalogStatus),
            new StubShellLaunchService(),
            new NotConfiguredReleaseService(),
            new LauncherUpdateTargetFactory(),
            new TestLauncherUpdateOperationGate(),
            startupDiagnosticReader: startupDiagnosticReader);

    private sealed class StubLauncherProfileCatalog(IReadOnlyList<LauncherProfileDefinition> profiles)
        : ILauncherProfileCatalog
    {
        public IReadOnlyList<LauncherProfileDefinition> LoadProfiles() => profiles;
    }

    private sealed class StubLocalAccountAuthService(
        LauncherAuthenticationResult loginResult,
        LauncherAccountCatalogStatus accountCatalogStatus)
        : ILocalLauncherAuthService
    {
        public LauncherAccountCatalogStatus AccountCatalogStatus => accountCatalogStatus;

        public LauncherAuthenticationResult Authenticate(string? userName, string? password) => loginResult;

        public LauncherAccountSetupResult InitializeLocalAccount(
            string? userName,
            string? displayName,
            string? newPassword,
            string? confirmPassword)
            => LauncherAccountSetupResult.Passed(new LauncherAccountRecord(
                userName ?? "operator",
                displayName ?? "operator",
                "hash",
                true));

        public LauncherPasswordChangeResult ChangePassword(
            string? userName,
            string? oldPassword,
            string? newPassword)
            => LauncherPasswordChangeResult.Passed();
    }

    private sealed class StubShellLaunchService : IShellLaunchService
    {
        public bool HasAnyRunningShellProcess() => false;

        public bool IsProfileRunning(LauncherProfileDefinition profile) => false;

        public Task<ShellLaunchResult> LaunchAsync(
            LauncherProfileDefinition profile,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ShellLaunchResult(false, []));
    }

    private sealed class NotConfiguredReleaseService : IEdgeReleaseService
    {
        public Task<EdgeReleaseCatalogResult> CheckReleaseCatalogAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeReleaseCatalogResult(
                EdgeReleaseCatalogState.NotConfigured,
                "stable",
                "win-x64",
                string.Empty,
                string.Empty,
                [],
                "not configured"));

        public Task<EdgePluginInstallResult> ApplyPluginVersionAsync(
            EdgeUpdateTarget target,
            string moduleId,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not configured"));

        public Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
            EdgeUpdateTarget target,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(false, "not configured"));

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            EdgeUpdateTarget target,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("not configured"));

        public Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeVersionReportResult.Failed("not configured"));
    }

    private sealed class TestLauncherUpdateOperationGate
        : ILauncherUpdateOperationGate
    {
        public IDisposable TryAcquire() => Lease.Instance;

        public IDisposable TryAcquireUpdate() => Lease.Instance;

        public string CreateShellLaunchReadyPath()
            => Path.Combine(
                Path.GetTempPath(),
                $"launcher-ui-{Guid.NewGuid():N}.json");

        private sealed class Lease : IDisposable
        {
            public static Lease Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
