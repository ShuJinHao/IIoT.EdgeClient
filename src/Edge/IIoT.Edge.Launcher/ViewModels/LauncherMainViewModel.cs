using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherMainViewModel : BaseNotifyPropertyChanged, IDisposable
{
    private readonly ILauncherProfileCatalog _profileCatalog;
    private readonly ILocalLauncherAuthService _authService;
    private readonly IShellLaunchService _launchService;
    private readonly ILauncherUpdateService _updateService;
    private readonly IAppLanguageService? _languageService;
    private readonly List<LauncherProfileDefinition> _allProfiles = [];

    private const string AuthErrorUserNameRequired = "\u8bf7\u8f93\u5165\u8d26\u53f7\u3002";
    private const string AuthErrorPasswordRequired = "\u8bf7\u8f93\u5165\u5bc6\u7801\u3002";
    private const string AuthErrorAccountConfigurationUnavailable = LocalLauncherAuthService.AccountConfigurationUnavailableError;
    private const string AuthErrorAccountDisabledOrMissing = "\u672c\u5730\u8d26\u53f7\u4e0d\u5b58\u5728\uff0c\u6216\u5df2\u88ab\u7981\u7528\u3002";
    private const string AuthErrorInvalidCredentials = "\u8d26\u53f7\u6216\u5bc6\u7801\u4e0d\u6b63\u786e\u3002";
    private const string AuthErrorNewPasswordRequired = "\u65b0\u5bc6\u7801\u4e0d\u80fd\u4e3a\u7a7a\u3002";
    private const string AuthErrorNewPasswordMinLength = "\u65b0\u5bc6\u7801\u81f3\u5c11\u9700\u8981 6 \u4f4d\u3002";
    private const string AuthErrorOldPasswordInvalid = "\u65e7\u5bc6\u7801\u6821\u9a8c\u5931\u8d25\u3002";

    private string _errorMessage = string.Empty;
    private string _statusKey = "Launcher_Status_Initial";
    private object[] _statusArgs = [];
    private string _statusMessage = string.Empty;
    private string _welcomeKey = "Launcher_Welcome_Anonymous";
    private object[] _welcomeArgs = [];
    private string _welcomeText = string.Empty;
    private string _profileSearchText = string.Empty;
    private string _profileSummaryKey = "Launcher_ProfileSummary_Zero";
    private object[] _profileSummaryArgs = [];
    private string _profileSummaryText = string.Empty;
    private string _updateStatusKey = "Launcher_Update_StatusInitial";
    private object[] _updateStatusArgs = [];
    private string _updateStatusMessage = string.Empty;
    private string _updateDetailText = string.Empty;
    private int _updateProgress;
    private bool _isAuthenticated;
    private bool _isBusy;
    private bool _isUpdateBusy;
    private bool _hasUpdateAvailable;
    private bool _isUpdateProgressVisible;

    public LauncherMainViewModel(
        ILauncherProfileCatalog profileCatalog,
        ILocalLauncherAuthService authService,
        IShellLaunchService launchService,
        ILauncherUpdateService? updateService = null,
        IAppLanguageService? languageService = null)
    {
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _updateService = updateService ?? NullLauncherUpdateService.Instance;
        _languageService = languageService;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged += OnLanguageChanged;
        }

        AppVersionText = BuildAppVersionText();
        RefreshLocalizedState();
    }

    public ObservableCollection<LauncherProfileDefinition> Profiles { get; } = [];

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string WelcomeText
    {
        get => _welcomeText;
        private set => SetProperty(ref _welcomeText, value);
    }

    public string ProfileSearchText
    {
        get => _profileSearchText;
        set
        {
            if (SetProperty(ref _profileSearchText, value))
            {
                ApplyProfileFilter();
            }
        }
    }

    public string ProfileSummaryText
    {
        get => _profileSummaryText;
        private set => SetProperty(ref _profileSummaryText, value);
    }

    public string AppVersionText { get; }

    public string UpdateStatusMessage
    {
        get => _updateStatusMessage;
        private set => SetProperty(ref _updateStatusMessage, value);
    }

    public string UpdateDetailText
    {
        get => _updateDetailText;
        private set => SetProperty(ref _updateDetailText, value);
    }

    public int UpdateProgress
    {
        get => _updateProgress;
        private set => SetProperty(ref _updateProgress, Math.Clamp(value, 0, 100));
    }

    public bool IsUpdateBusy
    {
        get => _isUpdateBusy;
        private set
        {
            if (SetProperty(ref _isUpdateBusy, value))
            {
                OnPropertyChanged(nameof(CanCheckUpdates));
                OnPropertyChanged(nameof(CanApplyUpdate));
            }
        }
    }

    public bool IsUpdateProgressVisible
    {
        get => _isUpdateProgressVisible;
        private set => SetProperty(ref _isUpdateProgressVisible, value);
    }

    public bool CanCheckUpdates => !IsUpdateBusy;

    public bool CanApplyUpdate => _hasUpdateAvailable && !IsUpdateBusy;

    public string PlatformMetaText => Text("Launcher_Meta_Platform");

    public string MaintainerText => Text("Launcher_Meta_Maintainer");

    public string ArchitectureText => Text("Launcher_Meta_Architecture");

    public string LanguageToggleText => Text("Launcher_Language_ToggleTarget");

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set => SetProperty(ref _isAuthenticated, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public async Task LoginAsync(string? userName, string? password)
    {
        ErrorMessage = string.Empty;
        SetStatus("Launcher_Status_ValidatingLogin");
        IsBusy = true;

        try
        {
            await Task.Yield();

            var result = _authService.Authenticate(userName, password);
            if (!result.Success)
            {
                ResetToLoggedOutState();
                ErrorMessage = LocalizeAuthenticationError(result.ErrorMessage)
                    ?? Text("Launcher_Error_LoginFailed");
                SetStatus("Launcher_Status_LoginRetry");
                return;
            }

            _allProfiles.Clear();
            _allProfiles.AddRange(_profileCatalog.LoadProfiles());

            IsAuthenticated = true;
            SetWelcome(
                "Launcher_Welcome_Format",
                result.DisplayName ?? result.UserName ?? string.Empty);
            ProfileSearchText = string.Empty;
            ApplyProfileFilter();
            SetStatus("Launcher_Status_SelectProfile");
        }
        catch (Exception ex)
        {
            ResetToLoggedOutState();
            ErrorMessage = ex.Message;
            SetStatus("Launcher_Status_ProfileLoadFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ChangePasswordAsync(string? userName, string? oldPassword, string? newPassword)
    {
        ErrorMessage = string.Empty;
        SetStatus("Launcher_Status_ChangingPassword");
        IsBusy = true;

        try
        {
            await Task.Yield();
            var result = _authService.ChangePassword(userName, oldPassword, newPassword);
            if (!result.Success)
            {
                ErrorMessage = LocalizeAuthenticationError(result.ErrorMessage)
                    ?? Text("Launcher_Status_PasswordChangeFailed");
                SetStatus("Launcher_Status_PasswordRetry");
                return false;
            }

            SetStatus("Launcher_Status_PasswordChanged");
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            SetStatus("Launcher_Status_PasswordChangeFailed");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task LaunchAsync(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ErrorMessage = string.Empty;
        try
        {
            _launchService.Launch(profile);
            SetStatus(
                "Launcher_Status_LaunchSucceededFormat",
                profile.DisplayName,
                profile.MachineProfile);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            SetStatus(
                "Launcher_Status_LaunchFailedFormat",
                profile.DisplayName);
        }

        return Task.CompletedTask;
    }

    public async Task CheckForUpdatesAsync()
    {
        SetUpdateStatus("Launcher_Update_StatusChecking");
        UpdateDetailText = string.Empty;
        UpdateProgress = 0;
        IsUpdateProgressVisible = false;
        SetHasUpdateAvailable(false);
        IsUpdateBusy = true;

        try
        {
            var result = await _updateService.CheckForUpdatesAsync().ConfigureAwait(true);
            ApplyUpdateCheckResult(result);
        }
        catch (OperationCanceledException)
        {
            SetUpdateStatus("Launcher_Update_StatusCanceled");
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    public async Task ApplyUpdateAsync()
    {
        if (_launchService.HasRunningShellProcess)
        {
            IsUpdateProgressVisible = false;
            UpdateProgress = 0;
            SetUpdateStatus("Launcher_Update_StatusShellRunning");
            UpdateDetailText = Text("Launcher_Update_ShellRunningDetail");
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

        SetUpdateStatus("Launcher_Update_StatusDownloading");
        UpdateDetailText = string.Empty;
        UpdateProgress = 0;
        IsUpdateProgressVisible = true;
        IsUpdateBusy = true;

        try
        {
            var progress = new Progress<int>(value => UpdateProgress = value);
            var result = await _updateService.DownloadAndApplyUpdateAsync(progress).ConfigureAwait(true);
            if (result.Started)
            {
                SetUpdateStatus("Launcher_Update_StatusApplying");
                return;
            }

            SetHasUpdateAvailable(false);
            SetUpdateStatus("Launcher_Update_StatusFailed");
            UpdateDetailText = result.ErrorMessage ?? Text("Launcher_Update_ErrorUnknown");
        }
        catch (OperationCanceledException)
        {
            SetUpdateStatus("Launcher_Update_StatusCanceled");
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    public string GetText(string key)
        => Text(key);

    private void ApplyProfileFilter()
    {
        var keyword = ProfileSearchText?.Trim();
        IEnumerable<LauncherProfileDefinition> filtered = _allProfiles;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filtered = filtered.Where(profile =>
                Contains(profile.DisplayName, keyword) ||
                Contains(profile.Description, keyword) ||
                Contains(profile.ProfileId, keyword) ||
                Contains(profile.MachineProfile, keyword));
        }

        Profiles.Clear();
        foreach (var profile in filtered)
        {
            Profiles.Add(profile);
        }

        if (_allProfiles.Count == 0)
        {
            SetProfileSummary("Launcher_ProfileSummary_Zero");
            return;
        }

        if (string.IsNullOrWhiteSpace(keyword))
        {
            SetProfileSummary("Launcher_ProfileSummary_AllFormat", _allProfiles.Count);
            return;
        }

        SetProfileSummary(
            "Launcher_ProfileSummary_FilteredFormat",
            Profiles.Count,
            _allProfiles.Count);
    }

    private void ResetToLoggedOutState()
    {
        IsAuthenticated = false;
        SetWelcome("Launcher_Welcome_Anonymous");
        ProfileSearchText = string.Empty;
        _allProfiles.Clear();
        Profiles.Clear();
        SetProfileSummary("Launcher_ProfileSummary_Zero");
    }

    public void Dispose()
    {
        if (_languageService is not null)
        {
            _languageService.LanguageChanged -= OnLanguageChanged;
        }
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

    private static bool Contains(string? source, string keyword)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static string BuildAppVersionText()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null)
        {
            return "v1.0.0";
        }

        return $"v{version.Major}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLocalizedState();
        OnPropertyChanged(nameof(PlatformMetaText));
        OnPropertyChanged(nameof(MaintainerText));
        OnPropertyChanged(nameof(ArchitectureText));
        OnPropertyChanged(nameof(LanguageToggleText));
    }

    private void RefreshLocalizedState()
    {
        StatusMessage = Format(_statusKey, _statusArgs);
        WelcomeText = Format(_welcomeKey, _welcomeArgs);
        ProfileSummaryText = Format(_profileSummaryKey, _profileSummaryArgs);
        UpdateStatusMessage = Format(_updateStatusKey, _updateStatusArgs);
    }

    private void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusMessage = Format(key, args);
    }

    private void SetWelcome(string key, params object[] args)
    {
        _welcomeKey = key;
        _welcomeArgs = args;
        WelcomeText = Format(key, args);
    }

    private void SetProfileSummary(string key, params object[] args)
    {
        _profileSummaryKey = key;
        _profileSummaryArgs = args;
        ProfileSummaryText = Format(key, args);
    }

    private void SetUpdateStatus(string key, params object[] args)
    {
        _updateStatusKey = key;
        _updateStatusArgs = args;
        UpdateStatusMessage = Format(key, args);
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

    private void ApplyUpdateCheckResult(LauncherUpdateCheckResult result)
    {
        SetHasUpdateAvailable(result.HasUpdate);
        UpdateDetailText = CompactUpdateDetail(result.ReleaseNotes ?? result.ErrorMessage);

        switch (result.State)
        {
            case LauncherUpdateCheckState.NotConfigured:
                SetUpdateStatus("Launcher_Update_StatusNotConfigured");
                break;
            case LauncherUpdateCheckState.NotInstalled:
                SetUpdateStatus("Launcher_Update_StatusNotInstalled");
                break;
            case LauncherUpdateCheckState.NoUpdate:
                SetUpdateStatus("Launcher_Update_StatusNoUpdate", result.CurrentVersion ?? AppVersionText);
                break;
            case LauncherUpdateCheckState.UpdateAvailable:
                SetUpdateStatus("Launcher_Update_StatusAvailable", result.TargetVersion ?? string.Empty);
                break;
            case LauncherUpdateCheckState.PendingRestart:
                SetUpdateStatus("Launcher_Update_StatusPendingRestart", result.TargetVersion ?? string.Empty);
                break;
            case LauncherUpdateCheckState.Failed:
                SetUpdateStatus("Launcher_Update_StatusFailed");
                break;
            default:
                SetUpdateStatus("Launcher_Update_StatusInitial");
                break;
        }
    }

    private static string CompactUpdateDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        var normalized = detail
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 180
            ? normalized
            : normalized[..180] + "...";
    }

    private string? LocalizeAuthenticationError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || _languageService is null)
        {
            return message;
        }

        return message.Trim() switch
        {
            AuthErrorUserNameRequired => Text("Launcher_Error_UserNameRequired"),
            AuthErrorPasswordRequired => Text("Launcher_Error_PasswordRequired"),
            AuthErrorAccountConfigurationUnavailable => Text("Launcher_Error_AccountConfigurationUnavailable"),
            AuthErrorAccountDisabledOrMissing => Text("Launcher_Error_AccountDisabledOrMissing"),
            AuthErrorInvalidCredentials => Text("Launcher_Error_InvalidCredentials"),
            AuthErrorNewPasswordRequired => Text("Launcher_Error_NewPasswordRequired"),
            AuthErrorNewPasswordMinLength => Text("Launcher_Error_NewPasswordMinLength"),
            AuthErrorOldPasswordInvalid => Text("Launcher_Error_OldPasswordInvalid"),
            _ => message
        };
    }

    private string Text(string key)
        => _languageService?.GetString(key, FallbackFor(key)) ?? StaticText(key, FallbackFor(key));

    private string Format(string key, params object[] args)
        => _languageService?.Format(key, FallbackFor(key), args)
            ?? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, StaticText(key, FallbackFor(key)), args);

    private static string FallbackFor(string key) => key switch
    {
        "Launcher_Welcome_Format" => "{0}",
        "Launcher_ProfileSummary_AllFormat" => "{0}",
        "Launcher_ProfileSummary_FilteredFormat" => "{0} / {1}",
        "Launcher_Status_LaunchSucceededFormat" => "{0} {1}",
        "Launcher_Status_LaunchFailedFormat" => "{0}",
        "Launcher_Update_StatusNoUpdate" => "{0}",
        "Launcher_Update_StatusAvailable" => "{0}",
        "Launcher_Update_StatusPendingRestart" => "{0}",
        "Launcher_Update_ShellRunningDetail" => string.Empty,
        _ => key
    };

    private static string StaticText(string key, string fallback)
    {
        var app = global::Avalonia.Application.Current;
        return app?.TryGetResource(key, null, out var value) == true
            && value is string text
            && !string.IsNullOrWhiteSpace(text)
                ? text
                : fallback;
    }

    private sealed class NullLauncherUpdateService : ILauncherUpdateService
    {
        public static readonly NullLauncherUpdateService Instance = new();

        public Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new LauncherUpdateCheckResult(LauncherUpdateCheckState.NotConfigured));

        public Task<LauncherUpdateApplyResult> DownloadAndApplyUpdateAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LauncherUpdateApplyResult(false, "Update source is not configured."));
    }
}
