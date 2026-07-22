using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherMainViewModel : BaseNotifyPropertyChanged, IDisposable
{
    private readonly ILauncherProfileCatalog _profileCatalog;
    private readonly IEdgeUpdateConfigurationProvider? _updateConfigurationProvider;
    private readonly ILauncherUpdateTargetFactory _targetFactory;
    private readonly ILocalLauncherAuthService _authService;
    private readonly IShellLaunchService _launchService;
    private readonly IEdgeHostUpdateService _updateService;
    private readonly IEdgeReleaseService _clientReleaseService;
    private readonly ILauncherProfileVisibilityService? _profileVisibilityService;
    private readonly IAppLanguageService? _languageService;
    private readonly List<LauncherProfileDefinition> _allProfiles = [];
    private readonly List<LauncherProfileCardViewModel> _allProfileCards = [];

    private const string AuthErrorUserNameRequired = "\u8bf7\u8f93\u5165\u8d26\u53f7\u3002";
    private const string AuthErrorPasswordRequired = "\u8bf7\u8f93\u5165\u5bc6\u7801\u3002";
    private const string AuthErrorAccountConfigurationUnavailable = LocalLauncherAuthService.AccountConfigurationUnavailableError;
    private const string AuthErrorAccountSetupUnavailable = LocalLauncherAuthService.AccountSetupUnavailableError;
    private const string AuthErrorPasswordResetRequired = LocalLauncherAuthService.PasswordResetRequiredError;
    private const string AuthErrorAccountLocked = LocalLauncherAuthService.AccountLockedError;
    private const string AuthErrorAccountDisabledOrMissing = "\u672c\u5730\u8d26\u53f7\u4e0d\u5b58\u5728\uff0c\u6216\u5df2\u88ab\u7981\u7528\u3002";
    private const string AuthErrorInvalidCredentials = "\u8d26\u53f7\u6216\u5bc6\u7801\u4e0d\u6b63\u786e\u3002";
    private const string AuthErrorDisplayNameRequired = LocalLauncherAuthService.DisplayNameRequiredError;
    private const string AuthErrorNewPasswordRequired = "\u65b0\u5bc6\u7801\u4e0d\u80fd\u4e3a\u7a7a\u3002";
    private const string AuthErrorNewPasswordMinLength = LauncherPasswordPolicy.RequirementMessage;
    private const string AuthErrorOldPasswordInvalid = "\u65e7\u5bc6\u7801\u6821\u9a8c\u5931\u8d25\u3002";
    private const string AuthErrorPasswordConfirmationMismatch = LocalLauncherAuthService.PasswordConfirmationMismatchError;

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
    private string _accountSetupUserName = string.Empty;
    private string _accountSetupDisplayName = string.Empty;
    private LauncherProfileCardViewModel? _selectedUpdateProfile;
    private bool _isAuthenticated;
    private bool _accountSetupRequired;
    private bool _accountCatalogCorrupt;
    private bool _isBusy;

    public LauncherMainViewModel(
        ILauncherProfileCatalog profileCatalog,
        ILocalLauncherAuthService authService,
        IShellLaunchService launchService,
        IEdgeHostUpdateService? updateService = null,
        IAppLanguageService? languageService = null,
        IEdgeReleaseService? clientReleaseService = null,
        IEdgeUpdateConfigurationProvider? updateConfigurationProvider = null,
        ILauncherUpdateTargetFactory? targetFactory = null,
        ILauncherProfileVisibilityService? profileVisibilityService = null)
    {
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _updateConfigurationProvider = updateConfigurationProvider;
        _targetFactory = targetFactory ?? new LauncherUpdateTargetFactory();
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _updateService = updateService ?? NullEdgeHostUpdateService.Instance;
        _clientReleaseService = clientReleaseService ?? NullEdgeReleaseService.Instance;
        _profileVisibilityService = profileVisibilityService;
        _languageService = languageService;
        HostUpdatePanel = new LauncherHostUpdatePanelViewModel(
            _updateService,
            _launchService,
            languageService);
        ClientReleasePanel = new LauncherClientReleasePanelViewModel(
            _clientReleaseService,
            _targetFactory,
            _launchService,
            languageService);
        HostUpdatePanel.PropertyChanged += OnHostUpdatePanelChanged;
        ClientReleasePanel.PropertyChanged += OnClientReleasePanelChanged;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged += OnLanguageChanged;
        }

        AppVersionText = BuildAppVersionText();
        _accountSetupUserName = Text("Launcher_AccountSetup_DefaultUserName");
        _accountSetupDisplayName = Text("Launcher_AccountSetup_DefaultDisplayName");
        RefreshAccountCatalogState();
        RefreshLocalizedState();
        RebuildUpdateRows();
    }

    // 只显示当前首装选装或本机已安装插件对应的 profile；没有可判断的插件信息则回退显示全部，
    // 避免 Launcher 空屏(客户端规则·启动红线：必须能启动到可登录、可诊断、可修配置的 UI)。
    private sealed record LauncherLoginLoadResult(
        LauncherAuthenticationResult Authentication,
        IReadOnlyList<LauncherProfileDefinition> Profiles);

    private sealed record LauncherSetupLoadResult(
        LauncherAccountSetupResult Setup,
        IReadOnlyList<LauncherProfileDefinition> Profiles);

    private LauncherLoginLoadResult LoadLoginState(string? userName, string? password)
    {
        var authentication = _authService.Authenticate(userName, password);
        if (!authentication.Success)
        {
            return new LauncherLoginLoadResult(authentication, []);
        }

        var profiles = SelectVisibleProfiles(_profileCatalog.LoadProfiles()).ToArray();
        return new LauncherLoginLoadResult(authentication, profiles);
    }

    private LauncherSetupLoadResult LoadSetupState(
        string? userName,
        string? displayName,
        string? newPassword,
        string? confirmPassword)
    {
        var setup = _authService.InitializeLocalAccount(userName, displayName, newPassword, confirmPassword);
        if (!setup.Success)
        {
            return new LauncherSetupLoadResult(setup, []);
        }

        var profiles = SelectVisibleProfiles(_profileCatalog.LoadProfiles()).ToArray();
        return new LauncherSetupLoadResult(setup, profiles);
    }

    private IReadOnlyList<LauncherProfileDefinition> SelectVisibleProfiles(
        IReadOnlyList<LauncherProfileDefinition> profiles)
    {
        if (_profileVisibilityService is not null)
        {
            return _profileVisibilityService.SelectVisibleProfiles(profiles);
        }

        if (_updateConfigurationProvider is null)
        {
            return profiles;
        }

        var provisioned = profiles
            .Where(profile => _updateConfigurationProvider.Resolve(_targetFactory.Create(profile)).Success)
            .ToList();

        return provisioned.Count > 0 ? provisioned : profiles;
    }

    public ObservableCollection<LauncherProfileCardViewModel> Profiles { get; } = [];

    public LauncherHostUpdatePanelViewModel HostUpdatePanel { get; }

    public LauncherClientReleasePanelViewModel ClientReleasePanel { get; }

    public ObservableCollection<LauncherClientPluginItem> UpdateRows { get; } = [];

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

    public string AccountSetupUserName
    {
        get => _accountSetupUserName;
        set => SetProperty(ref _accountSetupUserName, value);
    }

    public string AccountSetupDisplayName
    {
        get => _accountSetupDisplayName;
        set => SetProperty(ref _accountSetupDisplayName, value);
    }

    public bool AccountSetupRequired
    {
        get => _accountSetupRequired;
        private set
        {
            if (SetProperty(ref _accountSetupRequired, value))
            {
                OnPropertyChanged(nameof(IsLoginMode));
            }
        }
    }

    public bool AccountCatalogCorrupt
    {
        get => _accountCatalogCorrupt;
        private set
        {
            if (SetProperty(ref _accountCatalogCorrupt, value))
            {
                OnPropertyChanged(nameof(IsLoginMode));
            }
        }
    }

    public bool IsLoginMode => !IsAuthenticated && !AccountSetupRequired && !AccountCatalogCorrupt;

    public LauncherProfileCardViewModel? SelectedUpdateProfile
    {
        get => _selectedUpdateProfile;
        set
        {
            if (SetProperty(ref _selectedUpdateProfile, value))
            {
                _ = CheckSelectedProfilePluginsAsync();
            }
        }
    }

    public string AppVersionText { get; }

    public string PlatformMetaText => Text("Launcher_Meta_Platform");

    public string MaintainerText => Text("Launcher_Meta_Maintainer");

    public string ArchitectureText => Text("Launcher_Meta_Architecture");

    public string LanguageToggleText => Text("Launcher_Language_ToggleTarget");

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set
        {
            if (SetProperty(ref _isAuthenticated, value))
            {
                OnPropertyChanged(nameof(IsLoginMode));
            }
        }
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
            var loginState = await Task.Run(() => LoadLoginState(userName, password));
            var result = loginState.Authentication;
            if (!result.Success)
            {
                ResetToLoggedOutState();
                ErrorMessage = LocalizeAuthenticationError(result.ErrorMessage)
                    ?? Text("Launcher_Error_LoginFailed");
                SetStatus("Launcher_Status_LoginRetry");
                return;
            }

            ApplyAuthenticatedState(
                result.DisplayName ?? result.UserName ?? string.Empty,
                loginState.Profiles);
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

    public async Task<bool> InitializeLocalAccountAsync(
        string? userName,
        string? displayName,
        string? newPassword,
        string? confirmPassword)
    {
        ErrorMessage = string.Empty;
        SetStatus("Launcher_Status_AccountSetupSaving");
        IsBusy = true;

        try
        {
            var setupState = await Task.Run(() => LoadSetupState(
                userName,
                displayName,
                newPassword,
                confirmPassword));
            if (!setupState.Setup.Success || setupState.Setup.Account is null)
            {
                ErrorMessage = LocalizeAuthenticationError(setupState.Setup.ErrorMessage)
                    ?? Text("Launcher_Status_AccountSetupFailed");
                RefreshAccountCatalogState();
                SetStatus("Launcher_Status_AccountSetupRetry");
                return false;
            }

            ApplyAuthenticatedState(setupState.Setup.Account.DisplayName, setupState.Profiles);
            return true;
        }
        catch (Exception ex)
        {
            ResetToLoggedOutState();
            ErrorMessage = ex.Message;
            SetStatus("Launcher_Status_ProfileLoadFailed");
            return false;
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
            var result = await Task.Run(() => _authService.ChangePassword(userName, oldPassword, newPassword));
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
            _ = ClientReleasePanel.ReportProfilesSilentlyAsync([profile]);
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

    public async Task LaunchProfileCardAsync(LauncherProfileCardViewModel card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (_launchService.IsProfileRunning(card.Profile))
        {
            card.SetRunning();
            SetStatus("Launcher_ProfileCard_DetailRunning");
            return;
        }

        await LaunchAsync(card.Profile).ConfigureAwait(true);
    }

    public async Task RefreshUpdateCenterAsync()
    {
        await HostUpdatePanel.CheckForUpdatesAsync().ConfigureAwait(true);
        await CheckSelectedProfilePluginsAsync().ConfigureAwait(true);
    }

    public string GetText(string key)
        => Text(key);

    private void ApplyProfileFilter()
    {
        var keyword = ProfileSearchText?.Trim();
        IEnumerable<LauncherProfileCardViewModel> filtered = _allProfileCards;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filtered = filtered.Where(card =>
                Contains(card.DisplayName, keyword) ||
                Contains(card.Description, keyword) ||
                Contains(card.ProfileId, keyword) ||
                Contains(card.MachineProfile, keyword));
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
        _allProfileCards.Clear();
        Profiles.Clear();
        SelectedUpdateProfile = null;
        ClientReleasePanel.Reset();
        SetProfileSummary("Launcher_ProfileSummary_Zero");
    }

    private void ApplyAuthenticatedState(
        string displayName,
        IReadOnlyList<LauncherProfileDefinition> profiles)
    {
        _allProfiles.Clear();
        _allProfiles.AddRange(profiles);
        _allProfileCards.Clear();
        foreach (var profile in _allProfiles)
        {
            var card = new LauncherProfileCardViewModel(profile, _languageService);
            card.SetReady();
            _allProfileCards.Add(card);
        }

        AccountSetupRequired = false;
        AccountCatalogCorrupt = false;
        IsAuthenticated = true;
        SetWelcome("Launcher_Welcome_Format", displayName);
        ProfileSearchText = string.Empty;
        ApplyProfileFilter();
        SelectedUpdateProfile = _allProfileCards.FirstOrDefault();
        SetStatus("Launcher_Status_SelectProfile");
        _ = HostUpdatePanel.CheckForUpdatesAsync();
        _ = ClientReleasePanel.ReportProfilesSilentlyAsync(_allProfiles.ToArray());
    }

    private void RefreshAccountCatalogState()
    {
        LauncherAccountCatalogStatus status;
        try
        {
            status = _authService.AccountCatalogStatus;
        }
        catch
        {
            status = LauncherAccountCatalogStatus.Corrupt;
        }

        AccountSetupRequired = status is LauncherAccountCatalogStatus.Missing
            or LauncherAccountCatalogStatus.Empty
            or LauncherAccountCatalogStatus.NeedsInitialSetup;
        AccountCatalogCorrupt = status == LauncherAccountCatalogStatus.Corrupt;

        if (AccountSetupRequired)
        {
            SetStatus("Launcher_Status_AccountSetupRequired");
            return;
        }

        if (AccountCatalogCorrupt)
        {
            SetStatus("Launcher_Status_AccountCatalogCorrupt");
            return;
        }

        if (!IsAuthenticated)
        {
            SetStatus("Launcher_Status_Initial");
        }
    }

    public void Dispose()
    {
        if (_languageService is not null)
        {
            _languageService.LanguageChanged -= OnLanguageChanged;
        }

        HostUpdatePanel.PropertyChanged -= OnHostUpdatePanelChanged;
        ClientReleasePanel.PropertyChanged -= OnClientReleasePanelChanged;
        HostUpdatePanel.Dispose();
        ClientReleasePanel.Dispose();
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
        foreach (var card in _allProfileCards)
        {
            card.RefreshLocalizedState();
        }

        RebuildUpdateRows();
    }

    private void RefreshLocalizedState()
    {
        StatusMessage = Format(_statusKey, _statusArgs);
        WelcomeText = Format(_welcomeKey, _welcomeArgs);
        ProfileSummaryText = Format(_profileSummaryKey, _profileSummaryArgs);
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

    private void OnHostUpdatePanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HostUpdatePanel.StatusMessage)
            or nameof(HostUpdatePanel.CanApplyUpdate))
        {
            RebuildUpdateRows();
        }
    }

    private void OnClientReleasePanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClientReleasePanel.IsVisible)
            or nameof(ClientReleasePanel.Components))
        {
            RebuildUpdateRows();
        }
    }

    private void RebuildUpdateRows()
    {
        UpdateRows.Clear();
        var catalogRows = ClientReleasePanel.Components
            .OrderBy(static component => component.ComponentKind == EdgeComponentKind.Host ? 0 : 1)
            .ThenBy(static component => component.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(BuildCatalogUpdateRow)
            .Where(static row => row is not null)
            .Cast<LauncherClientPluginItem>()
            .ToArray();

        if (catalogRows.Length == 0)
        {
            UpdateRows.Add(HostUpdatePanel.CreateHostRow());
            return;
        }

        foreach (var row in catalogRows)
        {
            UpdateRows.Add(row);
        }
    }

    public async Task ExecuteUpdateRowActionAsync(LauncherClientPluginItem row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.VersionOption is not null && row.CanInstallOrUpdate)
        {
            await ClientReleasePanel.ApplyVersionAsync(row.VersionOption).ConfigureAwait(true);
            RebuildUpdateRows();
            return;
        }

        if (string.Equals(row.ModuleId, LauncherHostUpdatePanelViewModel.HostRowModuleId, StringComparison.Ordinal))
        {
            await HostUpdatePanel.ApplyUpdateAsync().ConfigureAwait(true);
            RebuildUpdateRows();
            return;
        }

        return;
    }

    private async Task CheckSelectedProfilePluginsAsync()
    {
        if (_allProfiles.Count == 0)
        {
            ClientReleasePanel.Reset();
            RebuildUpdateRows();
            return;
        }

        try
        {
            await ClientReleasePanel.CheckAsync(_allProfiles.ToArray()).ConfigureAwait(true);
            RebuildUpdateRows();
        }
        catch
        {
            // 更新栏检查是非阻断链路，失败只体现在更新栏状态，不能影响工序启动。
            RebuildUpdateRows();
        }
    }

    private LauncherClientPluginItem? BuildCatalogUpdateRow(LauncherVersionComponentItem component)
    {
        var option = SelectUpdateCenterOption(component);
        var currentVersion = string.IsNullOrWhiteSpace(component.CurrentVersion)
            ? Text("Launcher_ClientRelease_Plugin_NotInstalled")
            : component.CurrentVersion;
        var targetVersion = option?.Version ?? currentVersion;
        var canUpdate = option is not null
                        && option.CanApply
                        && option.Status is EdgeVersionStatus.NotInstalled or EdgeVersionStatus.Newer;
        var status = option?.Status ?? EdgeVersionStatus.Current;
        var packageSizeText = string.IsNullOrWhiteSpace(option?.PackageSizeText)
            ? "-"
            : option.PackageSizeText;
        var releaseNotesText = ResolveUpdateRowReleaseNotes(option);

        return new LauncherClientPluginItem(
            component.ModuleId,
            ResolveUpdateRowDisplayName(component),
            currentVersion,
            targetVersion,
            packageSizeText,
            releaseNotesText,
            canUpdate,
            ResolveUpdateRowStatusKind(status),
            ResolveUpdateRowStatusText(status),
            canUpdate && option is not null
                ? option.ActionText
                : ResolveUpdateRowActionText(status),
            status,
            canUpdate ? option : null,
            component.ComponentKindText,
            option?.PublishedAtText ?? string.Empty,
            releaseNotesText,
            component,
            Format("Launcher_UpdateCenter_ButtonViewHistory", component.Versions.Count),
            Text("Launcher_UpdateCenter_NoHistory"));
    }

    private string ResolveUpdateRowDisplayName(LauncherVersionComponentItem component)
    {
        if (component.ComponentKind == EdgeComponentKind.Host)
        {
            return Text("Launcher_UpdateCenter_HostTitle");
        }

        return string.IsNullOrWhiteSpace(component.DisplayName)
            ? component.ModuleId
            : component.DisplayName;
    }

    private static LauncherVersionOptionItem? SelectUpdateCenterOption(LauncherVersionComponentItem component)
        => component.Versions.FirstOrDefault(static option => option.Status == EdgeVersionStatus.Newer)
           ?? component.Versions.FirstOrDefault(static option => option.Status == EdgeVersionStatus.NotInstalled)
           ?? component.Versions.FirstOrDefault(static option => option.Status == EdgeVersionStatus.Current)
           ?? component.Versions.FirstOrDefault(static option => option.Status == EdgeVersionStatus.Incompatible)
           ?? component.Versions.FirstOrDefault();

    private static string ResolveUpdateRowReleaseNotes(LauncherVersionOptionItem? option)
    {
        if (option is null)
        {
            return string.Empty;
        }

        return !string.IsNullOrWhiteSpace(option.CompatibilityIssue)
            ? option.CompatibilityIssue
            : option.ReleaseNotes;
    }

    private string ResolveUpdateRowStatusKind(EdgeVersionStatus status)
        => status switch
        {
            EdgeVersionStatus.Current => "Running",
            EdgeVersionStatus.Newer or EdgeVersionStatus.NotInstalled => "Warning",
            EdgeVersionStatus.Incompatible => "Error",
            EdgeVersionStatus.InstalledNewer => "Info",
            EdgeVersionStatus.Older or EdgeVersionStatus.Deprecated => "Default",
            _ => "Default"
        };

    private string ResolveUpdateRowStatusText(EdgeVersionStatus status)
        => status switch
        {
            EdgeVersionStatus.NotInstalled => Text("Launcher_ClientRelease_Plugin_StatusNotInstalled"),
            EdgeVersionStatus.Newer => Text("Launcher_ClientRelease_Plugin_StatusUpdateAvailable"),
            EdgeVersionStatus.Current => Text("Launcher_ProfileCard_StatusLatest"),
            EdgeVersionStatus.InstalledNewer => Text("Launcher_ClientRelease_Plugin_StatusInstalledNewer"),
            EdgeVersionStatus.Incompatible => Text("Launcher_ClientRelease_Plugin_StatusIncompatible"),
            EdgeVersionStatus.Deprecated => Text("Launcher_VersionManagement_StatusDeprecated"),
            EdgeVersionStatus.Older => Text("Launcher_VersionManagement_StatusOlder"),
            _ => Text("Launcher_ClientRelease_Plugin_StatusUnknown")
        };

    private string ResolveUpdateRowActionText(EdgeVersionStatus status)
        => status switch
        {
            EdgeVersionStatus.Current => Text("Launcher_ProfileCard_StatusLatest"),
            EdgeVersionStatus.Incompatible => Text("Launcher_VersionManagement_ButtonUnavailable"),
            _ => Text("Launcher_ClientRelease_ButtonNoAction")
        };

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
            AuthErrorAccountSetupUnavailable => Text("Launcher_Error_AccountSetupUnavailable"),
            AuthErrorPasswordResetRequired => Text("Launcher_Error_PasswordResetRequired"),
            AuthErrorAccountLocked => Text("Launcher_Error_AccountLocked"),
            AuthErrorAccountDisabledOrMissing => Text("Launcher_Error_AccountDisabledOrMissing"),
            AuthErrorInvalidCredentials => Text("Launcher_Error_InvalidCredentials"),
            AuthErrorDisplayNameRequired => Text("Launcher_Error_DisplayNameRequired"),
            AuthErrorNewPasswordRequired => Text("Launcher_Error_NewPasswordRequired"),
            AuthErrorNewPasswordMinLength => Text("Launcher_Error_NewPasswordMinLength"),
            AuthErrorOldPasswordInvalid => Text("Launcher_Error_OldPasswordInvalid"),
            AuthErrorPasswordConfirmationMismatch => Text("Launcher_Error_PasswordConfirmationMismatch"),
            _ => message
        };
    }

    private string Text(string key)
        => LauncherText.Get(_languageService, key);

    private string Format(string key, params object[] args)
        => LauncherText.Format(_languageService, key, args);

    private sealed class NullEdgeHostUpdateService : IEdgeHostUpdateService
    {
        public static readonly NullEdgeHostUpdateService Instance = new();

        public Task<EdgeHostUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateCheckResult(EdgeHostUpdateCheckState.NotConfigured));

        public Task<EdgeHostUpdateApplyResult> DownloadAndApplyUpdateAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(false, "Update source is not configured."));

        public Task<EdgeHostUpdateApplyResult> ApplyVersionAsync(
            EdgeHostVersionRelease release,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(false, "Update source is not configured."));
    }

    private sealed class NullEdgeReleaseService : IEdgeReleaseService
    {
        public static readonly NullEdgeReleaseService Instance = new();

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
                "CloudApi 配置不可用。"));

        public Task<EdgePluginInstallResult> ApplyPluginVersionAsync(
            EdgeUpdateTarget target,
            string moduleId,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("CloudApi 配置不可用。"));

        public Task<EdgeHostUpdateApplyResult> ApplyHostVersionAsync(
            EdgeUpdateTarget target,
            string version,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new EdgeHostUpdateApplyResult(false, "CloudApi 配置不可用。"));

        public Task<EdgePluginInstallResult> ApplyVersionCompositionAsync(
            EdgeUpdateTarget target,
            EdgeVersionSelection selection,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgePluginInstallResult.Failed("CloudApi 配置不可用。"));

        public Task<EdgeVersionReportResult> ReportCurrentVersionsAsync(
            EdgeUpdateTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EdgeVersionReportResult.Failed("CloudApi 配置不可用。"));
    }
}
