using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
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
    private readonly IEdgeReleaseService _clientReleaseService;
    private readonly ILauncherProfileVisibilityService? _profileVisibilityService;
    private readonly ILauncherStartupDiagnosticReader? _startupDiagnosticReader;
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
    private string _startupDiagnosticsText = string.Empty;
    private LauncherProfileCardViewModel? _selectedUpdateProfile;
    private bool _isAuthenticated;
    private bool _accountSetupRequired;
    private bool _accountCatalogCorrupt;
    private bool _isBusy;
    private bool _hasStartupDiagnostics;

    public LauncherMainViewModel(
        ILauncherProfileCatalog profileCatalog,
        ILocalLauncherAuthService authService,
        IShellLaunchService launchService,
        IEdgeReleaseService clientReleaseService,
        ILauncherUpdateTargetFactory targetFactory,
        ILauncherUpdateOperationGate updateOperationGate,
        IAppLanguageService? languageService = null,
        IEdgeUpdateConfigurationProvider? updateConfigurationProvider = null,
        ILauncherProfileVisibilityService? profileVisibilityService = null,
        ILauncherStartupDiagnosticReader? startupDiagnosticReader = null)
    {
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _updateConfigurationProvider = updateConfigurationProvider;
        _targetFactory = targetFactory ?? throw new ArgumentNullException(nameof(targetFactory));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _clientReleaseService = clientReleaseService ?? throw new ArgumentNullException(nameof(clientReleaseService));
        _profileVisibilityService = profileVisibilityService;
        _startupDiagnosticReader = startupDiagnosticReader;
        _languageService = languageService;
        ClientReleasePanel = new LauncherClientReleasePanelViewModel(
            _clientReleaseService,
            _targetFactory,
            _launchService,
            updateOperationGate,
            languageService);
        ClientReleasePanel.PropertyChanged += OnClientReleasePanelChanged;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged += OnLanguageChanged;
        }
        if (_startupDiagnosticReader is not null)
        {
            _startupDiagnosticReader.Changed += OnStartupDiagnosticsChanged;
        }

        AppVersionText = BuildAppVersionText();
        _accountSetupUserName = Text("Launcher_AccountSetup_DefaultUserName");
        _accountSetupDisplayName = Text("Launcher_AccountSetup_DefaultDisplayName");
        RefreshAccountCatalogState();
        RefreshLocalizedState();
        RefreshStartupDiagnostics();
        RebuildUpdateRows();
    }

    // profile 可见性优先由 Cloud 选择清单裁决；清单不可用时只保留维护入口，
    // 避免把本地目录投放误当成生产授权。
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

    public string StartupDiagnosticsText
    {
        get => _startupDiagnosticsText;
        private set => SetProperty(ref _startupDiagnosticsText, value);
    }

    public bool HasStartupDiagnostics
    {
        get => _hasStartupDiagnostics;
        private set => SetProperty(ref _hasStartupDiagnostics, value);
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
            ErrorMessage = Format(
                "Launcher_Error_LocalOperationFailedSafeFormat",
                ex.GetType().Name);
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
            ErrorMessage = Format(
                "Launcher_Error_LocalOperationFailedSafeFormat",
                ex.GetType().Name);
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
            ErrorMessage = Format(
                "Launcher_Error_LocalOperationFailedSafeFormat",
                ex.GetType().Name);
            SetStatus("Launcher_Status_PasswordChangeFailed");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LaunchAsync(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ErrorMessage = string.Empty;
        try
        {
            var result = await _launchService.LaunchAsync(profile).ConfigureAwait(true);
            if (result.ReadyWithDiagnostics)
            {
                SetStatus(
                    "Launcher_Status_LaunchSucceededWithDiagnosticsFormat",
                    profile.DisplayName,
                    string.Join(", ", result.Diagnostics.Select(static item => item.ReasonCode)));
            }
            else
            {
                SetStatus(
                    "Launcher_Status_LaunchSucceededFormat",
                    profile.DisplayName,
                    profile.MachineProfile);
            }
            _ = ClientReleasePanel.ReportProfilesSilentlyAsync([profile]);
        }
        catch (Exception ex)
        {
            ErrorMessage = Format(
                "Launcher_Error_ShellLaunchFailedSafeFormat",
                ex.GetType().Name);
            SetStatus(
                "Launcher_Status_LaunchFailedFormat",
                profile.DisplayName);
        }
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
        if (_startupDiagnosticReader is not null)
        {
            _startupDiagnosticReader.Changed -= OnStartupDiagnosticsChanged;
        }

        ClientReleasePanel.PropertyChanged -= OnClientReleasePanelChanged;
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
            return "v2.0.0";
        }

        return $"v{version.Major}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
        => RunOnUiThread(() =>
        {
            RefreshLocalizedState();
            RefreshStartupDiagnostics();
            OnPropertyChanged(nameof(PlatformMetaText));
            OnPropertyChanged(nameof(MaintainerText));
            OnPropertyChanged(nameof(ArchitectureText));
            OnPropertyChanged(nameof(LanguageToggleText));
            foreach (var card in _allProfileCards)
            {
                card.RefreshLocalizedState();
            }

            RebuildUpdateRows();
        });

    private void OnStartupDiagnosticsChanged(object? sender, EventArgs e)
        => RunOnUiThread(RefreshStartupDiagnostics);

    private static void RunOnUiThread(Action action)
    {
        if (global::Avalonia.Application.Current is null
            || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private void RefreshLocalizedState()
    {
        StatusMessage = Format(_statusKey, _statusArgs);
        WelcomeText = Format(_welcomeKey, _welcomeArgs);
        ProfileSummaryText = Format(_profileSummaryKey, _profileSummaryArgs);
    }

    private void RefreshStartupDiagnostics()
    {
        var diagnostics = _startupDiagnosticReader?.Snapshot ?? [];
        HasStartupDiagnostics = diagnostics.Count > 0;
        StartupDiagnosticsText = diagnostics.Count == 0
            ? string.Empty
            : Format(
                "Launcher_StartupDiagnostics_Format",
                string.Join(
                    ", ",
                    diagnostics.Select(static diagnostic =>
                        string.IsNullOrWhiteSpace(diagnostic.Subject)
                            ? $"{diagnostic.ReasonCode} → {diagnostic.RepairTarget}"
                            : $"{diagnostic.ReasonCode}[{diagnostic.Subject}] → {diagnostic.RepairTarget}")));
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
        var unavailableText = Text("Launcher_UpdateCenter_Unavailable");
        var targetVersion = component.IsCatalogAvailable
            ? option?.Version ?? currentVersion
            : unavailableText;
        var canUpdate = component.IsCatalogAvailable
                        && option is not null
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
            component.IsCatalogAvailable ? ResolveUpdateRowStatusKind(status) : "Default",
            component.IsCatalogAvailable ? ResolveUpdateRowStatusText(status) : unavailableText,
            !component.IsCatalogAvailable
                ? unavailableText
                : canUpdate && option is not null
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
}
