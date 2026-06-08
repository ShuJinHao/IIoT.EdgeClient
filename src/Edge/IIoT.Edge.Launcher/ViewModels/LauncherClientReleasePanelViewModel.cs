using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherClientReleasePanelViewModel : BaseNotifyPropertyChanged, IDisposable
{
    private readonly ILauncherClientReleaseService _clientReleaseService;
    private readonly IShellLaunchService _launchService;
    private readonly IAppLanguageService? _languageService;
    private LauncherProfileDefinition? _activeProfile;
    private string _statusKey = "Launcher_ClientRelease_StatusInitial";
    private object[] _statusArgs = [];
    private string _statusMessage = string.Empty;
    private string _detailText = string.Empty;
    private int _progress;
    private bool _isBusy;
    private bool _isProgressVisible;
    private bool _isVisible;

    public LauncherClientReleasePanelViewModel(
        ILauncherClientReleaseService clientReleaseService,
        IShellLaunchService launchService,
        IAppLanguageService? languageService = null)
    {
        _clientReleaseService = clientReleaseService ?? throw new ArgumentNullException(nameof(clientReleaseService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _languageService = languageService;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged += OnLanguageChanged;
        }

        RefreshLocalizedState();
    }

    public ObservableCollection<LauncherClientPluginItem> Plugins { get; } = [];

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string DetailText
    {
        get => _detailText;
        private set => SetProperty(ref _detailText, value);
    }

    public int Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, Math.Clamp(value, 0, 100));
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanCheckReleases));
            }
        }
    }

    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        private set => SetProperty(ref _isProgressVisible, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public bool CanCheckReleases => !IsBusy;

    public async Task CheckAsync(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        _activeProfile = profile;
        IsVisible = true;
        IsProgressVisible = false;
        Progress = 0;
        DetailText = string.Empty;
        SetStatus("Launcher_ClientRelease_StatusChecking", profile.DisplayName);
        IsBusy = true;

        try
        {
            var result = await _clientReleaseService.CheckAsync(profile).ConfigureAwait(true);
            ApplyCheckResult(result);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Launcher_ClientRelease_StatusCanceled");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task InstallOrUpdateAsync(LauncherClientPluginItem plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        if (_activeProfile is null)
        {
            SetStatus("Launcher_ClientRelease_StatusNoProfile");
            return;
        }

        if (_launchService.HasRunningShellProcess)
        {
            IsProgressVisible = false;
            Progress = 0;
            SetStatus("Launcher_ClientRelease_StatusShellRunning");
            DetailText = LauncherText.Get(_languageService, "Launcher_ClientRelease_ShellRunningDetail");
            return;
        }

        if (!plugin.CanInstallOrUpdate)
        {
            return;
        }

        IsProgressVisible = true;
        Progress = 0;
        DetailText = string.Empty;
        SetStatus("Launcher_ClientRelease_StatusInstalling", plugin.DisplayName);
        IsBusy = true;

        try
        {
            var progress = new Progress<int>(value => Progress = value);
            var result = await _clientReleaseService
                .InstallOrUpdatePluginAsync(_activeProfile, plugin.ModuleId, progress)
                .ConfigureAwait(true);
            if (!result.Success)
            {
                SetStatus("Launcher_ClientRelease_StatusFailed");
                DetailText = LauncherText.Compact(result.ErrorMessage);
                return;
            }

            SetStatus(
                "Launcher_ClientRelease_StatusInstalled",
                string.Join(", ", result.InstalledModuleIds));
            await CheckAsync(_activeProfile).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Launcher_ClientRelease_StatusCanceled");
        }
        finally
        {
            IsBusy = false;
            IsProgressVisible = false;
        }
    }

    public async Task ReportProfilesSilentlyAsync(IReadOnlyList<LauncherProfileDefinition> profiles)
    {
        foreach (var profile in profiles)
        {
            try
            {
                _ = await _clientReleaseService.ReportCurrentVersionsAsync(profile).ConfigureAwait(false);
            }
            catch
            {
                // 版本上报是非阻断链路，失败不能影响 Launcher 登录或 Shell 启动。
            }
        }
    }

    public void Reset()
    {
        Plugins.Clear();
        _activeProfile = null;
        IsVisible = false;
        DetailText = string.Empty;
        Progress = 0;
        IsProgressVisible = false;
        SetStatus("Launcher_ClientRelease_StatusInitial");
    }

    public void Dispose()
    {
        if (_languageService is not null)
        {
            _languageService.LanguageChanged -= OnLanguageChanged;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLocalizedState();
        foreach (var plugin in Plugins)
        {
            plugin.StatusText = ResolvePluginStateText(plugin.State);
            plugin.ActionText = ResolvePluginActionText(plugin.State);
            if (plugin.State == LauncherPluginUpdateState.NotInstalled)
            {
                plugin.CurrentVersion = LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_NotInstalled");
            }
        }
    }

    private void RefreshLocalizedState()
        => StatusMessage = LauncherText.Format(_languageService, _statusKey, _statusArgs);

    private void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusMessage = LauncherText.Format(_languageService, key, args);
    }

    private void ApplyCheckResult(LauncherClientReleaseCheckResult result)
    {
        Plugins.Clear();
        foreach (var plan in result.Plugins)
        {
            Plugins.Add(new LauncherClientPluginItem(
                plan.Release.ModuleId,
                string.IsNullOrWhiteSpace(plan.Release.DisplayName) ? plan.Release.ModuleId : plan.Release.DisplayName,
                plan.InstalledPlugin?.Version ?? LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_NotInstalled"),
                plan.Release.Version,
                FormatPackageSize(plan.Release.PackageSize),
                plan.CompatibilityIssue ?? plan.Release.ReleaseNotes ?? string.Empty,
                plan.CanInstallOrUpdate,
                ResolvePluginStateKind(plan.State),
                ResolvePluginStateText(plan.State),
                ResolvePluginActionText(plan.State),
                plan.State));
        }

        DetailText = LauncherText.Compact(result.ErrorMessage);
        switch (result.State)
        {
            case LauncherClientReleaseCheckState.Succeeded:
                SetStatus(
                    "Launcher_ClientRelease_StatusReady",
                    result.HostVersion,
                    result.LatestHostVersion ?? result.HostVersion,
                    result.Plugins.Count);
                break;
            case LauncherClientReleaseCheckState.NotConfigured:
                SetStatus("Launcher_ClientRelease_StatusNotConfigured");
                break;
            case LauncherClientReleaseCheckState.BootstrapFailed:
                SetStatus("Launcher_ClientRelease_StatusBootstrapFailed");
                break;
            case LauncherClientReleaseCheckState.CatalogUnavailable:
                SetStatus("Launcher_ClientRelease_StatusCatalogFailed");
                break;
            default:
                SetStatus("Launcher_ClientRelease_StatusFailed");
                break;
        }
    }

    private string ResolvePluginStateText(LauncherPluginUpdateState state)
        => state switch
        {
            LauncherPluginUpdateState.NotInstalled => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusNotInstalled"),
            LauncherPluginUpdateState.UpdateAvailable => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusUpdateAvailable"),
            LauncherPluginUpdateState.Latest => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusLatest"),
            LauncherPluginUpdateState.InstalledNewer => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusInstalledNewer"),
            LauncherPluginUpdateState.Incompatible => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusIncompatible"),
            _ => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusUnknown")
        };

    private string ResolvePluginActionText(LauncherPluginUpdateState state)
        => state switch
        {
            LauncherPluginUpdateState.NotInstalled => LauncherText.Get(_languageService, "Launcher_ClientRelease_ButtonInstall"),
            LauncherPluginUpdateState.UpdateAvailable => LauncherText.Get(_languageService, "Launcher_ClientRelease_ButtonUpdate"),
            _ => LauncherText.Get(_languageService, "Launcher_ClientRelease_ButtonNoAction")
        };

    private static string ResolvePluginStateKind(LauncherPluginUpdateState state)
        => state switch
        {
            LauncherPluginUpdateState.Latest => "Success",
            LauncherPluginUpdateState.UpdateAvailable => "Warning",
            LauncherPluginUpdateState.Incompatible => "Danger",
            LauncherPluginUpdateState.NotInstalled => "Info",
            _ => "Default"
        };

    private static string FormatPackageSize(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        var mib = bytes / 1024d / 1024d;
        return mib >= 1
            ? $"{mib:0.0} MB"
            : $"{bytes / 1024d:0.0} KB";
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
