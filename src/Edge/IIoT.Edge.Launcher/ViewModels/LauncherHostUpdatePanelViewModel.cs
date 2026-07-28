using System.Runtime.CompilerServices;
using Avalonia.Threading;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherHostUpdatePanelViewModel : BaseNotifyPropertyChanged, IDisposable
{
    private readonly IEdgeHostUpdateService _updateService;
    private readonly IShellLaunchService _launchService;
    private readonly IAppLanguageService? _languageService;
    private readonly ILauncherUpdateOperationGate _updateOperationGate;
    private string _statusKey = "Launcher_Update_StatusInitial";
    private object[] _statusArgs = [];
    private string _statusMessage = string.Empty;
    private string _detailText = string.Empty;
    private int _progress;
    private bool _isBusy;
    private bool _hasUpdateAvailable;
    private bool _isProgressVisible;
    private string _currentVersion = "—";
    private string _targetVersion = "—";
    private EdgeHostUpdateCheckState _lastState = EdgeHostUpdateCheckState.NotConfigured;

    public const string HostRowModuleId = "__edge_host__";

    public LauncherHostUpdatePanelViewModel(
        IEdgeHostUpdateService updateService,
        IShellLaunchService launchService,
        IAppLanguageService? languageService = null,
        ILauncherUpdateOperationGate? updateOperationGate = null)
    {
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _languageService = languageService;
        _updateOperationGate = updateOperationGate ?? NoopLauncherUpdateOperationGate.Instance;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged += OnLanguageChanged;
        }

        RefreshLocalizedState();
    }

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
                OnPropertyChanged(nameof(CanCheckUpdates));
                OnPropertyChanged(nameof(CanApplyUpdate));
            }
        }
    }

    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        private set => SetProperty(ref _isProgressVisible, value);
    }

    public bool CanCheckUpdates => !IsBusy;

    public bool CanApplyUpdate => _hasUpdateAvailable && !IsBusy;

    public async Task CheckForUpdatesAsync()
    {
        SetStatus("Launcher_Update_StatusChecking");
        DetailText = string.Empty;
        Progress = 0;
        IsProgressVisible = false;
        SetHasUpdateAvailable(false);
        IsBusy = true;

        try
        {
            var result = await _updateService.CheckForUpdatesAsync().ConfigureAwait(true);
            ApplyUpdateCheckResult(result);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Launcher_Update_StatusCanceled");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ApplyUpdateAsync()
    {
        using var updateLease = _updateOperationGate.TryAcquireUpdate();
        if (updateLease is null)
        {
            IsProgressVisible = false;
            Progress = 0;
            SetStatus("Launcher_Update_StatusBusy");
            DetailText = LauncherText.Get(_languageService, "Launcher_Update_BusyDetail");
            return;
        }

        if (_launchService.HasAnyRunningShellProcess())
        {
            IsProgressVisible = false;
            Progress = 0;
            SetStatus("Launcher_Update_StatusShellRunning");
            DetailText = LauncherText.Get(_languageService, "Launcher_Update_ShellRunningDetail");
            return;
        }

        if (!CanApplyUpdate)
        {
            await CheckForUpdatesAsync().ConfigureAwait(true);
            if (!CanApplyUpdate)
            {
                return;
            }
        }

        SetStatus("Launcher_Update_StatusDownloading");
        DetailText = string.Empty;
        Progress = 0;
        IsProgressVisible = true;
        IsBusy = true;

        try
        {
            var progress = new Progress<int>(value => Progress = value);
            var result = await _updateService.DownloadAndApplyUpdateAsync(progress).ConfigureAwait(true);
            if (result.Started)
            {
                SetStatus("Launcher_Update_StatusApplying");
                return;
            }

            SetHasUpdateAvailable(false);
            SetStatus("Launcher_Update_StatusFailed");
            DetailText = result.ErrorMessage ?? LauncherText.Get(_languageService, "Launcher_Update_ErrorUnknown");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Launcher_Update_StatusCanceled");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        if (_languageService is not null)
        {
            _languageService.LanguageChanged -= OnLanguageChanged;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => RunOnUiThread(RefreshLocalizedState);

    private void RefreshLocalizedState()
        => StatusMessage = LauncherText.Format(_languageService, _statusKey, _statusArgs);

    private void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusMessage = LauncherText.Format(_languageService, key, args);
    }

    private void SetHasUpdateAvailable(bool value)
    {
        if (_hasUpdateAvailable == value)
        {
            return;
        }

        _hasUpdateAvailable = value;
        OnPropertyChanged(nameof(CanApplyUpdate));
    }

    private static void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private void ApplyUpdateCheckResult(EdgeHostUpdateCheckResult result)
    {
        SetHasUpdateAvailable(result.HasUpdate);
        _lastState = result.State;
        _currentVersion = string.IsNullOrWhiteSpace(result.CurrentVersion) ? "—" : result.CurrentVersion!;
        _targetVersion = string.IsNullOrWhiteSpace(result.TargetVersion) ? _currentVersion : result.TargetVersion!;
        DetailText = LauncherText.Compact(result.ReleaseNotes ?? result.ErrorMessage);

        switch (result.State)
        {
            case EdgeHostUpdateCheckState.NotConfigured:
                SetStatus("Launcher_Update_StatusNotConfigured");
                break;
            case EdgeHostUpdateCheckState.NotInstalled:
                SetStatus("Launcher_Update_StatusNotInstalled");
                break;
            case EdgeHostUpdateCheckState.NoUpdate:
                SetStatus("Launcher_Update_StatusNoUpdate", result.CurrentVersion ?? string.Empty);
                break;
            case EdgeHostUpdateCheckState.UpdateAvailable:
                SetStatus("Launcher_Update_StatusAvailable", result.TargetVersion ?? string.Empty);
                break;
            case EdgeHostUpdateCheckState.PendingRestart:
                SetStatus("Launcher_Update_StatusPendingRestart", result.TargetVersion ?? string.Empty);
                break;
            case EdgeHostUpdateCheckState.Failed:
                SetStatus("Launcher_Update_StatusFailed");
                break;
            default:
                SetStatus("Launcher_Update_StatusInitial");
                break;
        }
    }

    public LauncherClientPluginItem CreateHostRow()
    {
        var isCheckAvailable = _lastState is EdgeHostUpdateCheckState.NoUpdate
            or EdgeHostUpdateCheckState.UpdateAvailable
            or EdgeHostUpdateCheckState.PendingRestart;
        var unavailableText = LauncherText.Get(
            _languageService,
            "Launcher_UpdateCenter_Unavailable");
        var (statusKind, statusKey) = _lastState switch
        {
            EdgeHostUpdateCheckState.UpdateAvailable or EdgeHostUpdateCheckState.PendingRestart
                => ("Warning", "Launcher_ProfileCard_StatusUpdateAvailable"),
            EdgeHostUpdateCheckState.NoUpdate
                => ("Running", "Launcher_ProfileCard_StatusLatest"),
            _ => ("Default", "Launcher_UpdateCenter_HostStatusNotReady"),
        };

        return new LauncherClientPluginItem(
            HostRowModuleId,
            LauncherText.Get(_languageService, "Launcher_UpdateCenter_HostTitle"),
            _currentVersion,
            isCheckAvailable ? _targetVersion : unavailableText,
            string.Empty,
            string.Empty,
            CanApplyUpdate,
            statusKind,
            isCheckAvailable
                ? LauncherText.Get(_languageService, statusKey)
                : unavailableText,
            !isCheckAvailable
                ? unavailableText
                : _hasUpdateAvailable
                    ? LauncherText.Get(_languageService, "Launcher_UpdateCenter_ButtonHostUpdate")
                    : LauncherText.Get(_languageService, "Launcher_ProfileCard_StatusLatest"),
            _hasUpdateAvailable
                ? EdgeVersionStatus.Newer
                : EdgeVersionStatus.Current,
            componentKindText: LauncherText.Get(_languageService, "Launcher_VersionManagement_ComponentHost"),
            emptyHistoryText: LauncherText.Get(_languageService, "Launcher_UpdateCenter_NoHistory"));
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
