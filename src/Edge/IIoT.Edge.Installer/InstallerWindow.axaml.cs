using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IIoT.Edge.UI.Shared.Avalonia.Windowing;

namespace IIoT.Edge.Installer;

public partial class InstallerWindow : Window
{
    private const int WindowCornerRadius = 8;
    private readonly InstallerOptions _options;
    private CancellationTokenSource? _installCts;
    private string _installRoot;

    public InstallerWindow()
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
        _options = new InstallerOptions(null, false, false);
        _installRoot = SelfExtractor.GetDefaultInstallRoot();
        InitializeDefaults();
    }

    internal InstallerWindow(InstallerOptions options)
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
        _options = options;
        _installRoot = string.IsNullOrWhiteSpace(options.InstallTo)
            ? SelfExtractor.GetDefaultInstallRoot()
            : SelfExtractor.ResolveInstallRoot(options.InstallTo);
        InitializeDefaults();
    }

    private void InitializeDefaults()
    {
        InstallPathInput.Text = _installRoot;
        DefaultPathRun.Text = _installRoot;
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        _installCts?.Cancel();
        Close();
    }

    // ── Page 1: Welcome ──

    private void QuickInstallButton_Click(object? sender, RoutedEventArgs e)
    {
        StartInstall(_installRoot, createShortcut: true);
    }

    private void CustomInstallButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowPage(SettingsPage);
    }

    // ── Page 2: Settings ──

    private async void BrowseButton_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = Res("Installer_FolderPicker_Title", "选择安装目录"),
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            var path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                _installRoot = path;
                InstallPathInput.Text = path;
            }
        }
    }

    private void BackToWelcomeButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowPage(WelcomePage);
    }

    private void InstallButton_Click(object? sender, RoutedEventArgs e)
    {
        _installRoot = SelfExtractor.ResolveInstallRoot(InstallPathInput.Text);
        StartInstall(_installRoot, DesktopShortcutCheckBox.IsChecked == true);
    }

    // ── Page 3: Installing ──

    private async void StartInstall(string installRoot, bool createShortcut)
    {
        ShowPage(InstallingPage);

        _installCts = new CancellationTokenSource();
        var progress = new Progress<InstallerProgress>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                InstallProgressBar.IsIndeterminate = p.IsIndeterminate;
                InstallProgressBar.Value = p.Percent;
                ProgressPercentText.Text = p.IsIndeterminate ? string.Empty : $"{p.Percent}%";
                ProgressStatusText.Text = p.Status;
            });
        });

        var result = await InstallerService.RunGuiAsync(
            installRoot,
            createShortcut,
            progress,
            _installCts.Token,
            Res);

        if (result.Success)
        {
            CompleteMessageText.Text = string.Format(
                CultureInfo.CurrentCulture,
                Res("Installer_Complete_MessageFormat", "已安装到 {0}"),
                result.InstallRoot);
            ShowPage(CompletePage);
        }
        else
        {
            ErrorMessageText.Text = result.Message;
            ShowPage(ErrorPage);
        }
    }

    // ── Page 4: Complete ──

    private void LaunchButton_Click(object? sender, RoutedEventArgs e)
    {
        InstallerService.TryStartLauncher(InstallerService.GetLauncherPath(_installRoot));
        Close();
    }

    private void FinishButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // ── Error page ──

    private void RetryButton_Click(object? sender, RoutedEventArgs e)
    {
        ShowPage(WelcomePage);
    }

    // ── Navigation ──

    private void ShowPage(Grid page)
    {
        WelcomePage.IsVisible = page == WelcomePage;
        SettingsPage.IsVisible = page == SettingsPage;
        InstallingPage.IsVisible = page == InstallingPage;
        CompletePage.IsVisible = page == CompletePage;
        ErrorPage.IsVisible = page == ErrorPage;
    }

    private string Res(string key, string fallback)
        => this.TryFindResource(key, out var value) && value is string text
            ? text
            : fallback;
}
