using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherClientReleasePanelViewModel : BaseNotifyPropertyChanged, IDisposable
{
    private readonly IEdgeReleaseService _clientReleaseService;
    private readonly ILauncherUpdateTargetFactory _targetFactory;
    private readonly IShellLaunchService _launchService;
    private readonly IAppLanguageService? _languageService;
    private readonly Dictionary<string, LauncherProfileDefinition> _profileByModuleId = new(StringComparer.OrdinalIgnoreCase);
    private LauncherProfileDefinition? _activeProfile;
    private IReadOnlyList<LauncherProfileDefinition> _activeProfiles = [];
    private string _statusKey = "Launcher_ClientRelease_StatusInitial";
    private object[] _statusArgs = [];
    private string _statusMessage = string.Empty;
    private string _detailText = string.Empty;
    private int _progress;
    private bool _isBusy;
    private bool _isProgressVisible;
    private bool _isVisible;

    public LauncherClientReleasePanelViewModel(
        IEdgeReleaseService clientReleaseService,
        ILauncherUpdateTargetFactory targetFactory,
        IShellLaunchService launchService,
        IAppLanguageService? languageService = null)
    {
        _clientReleaseService = clientReleaseService ?? throw new ArgumentNullException(nameof(clientReleaseService));
        _targetFactory = targetFactory ?? throw new ArgumentNullException(nameof(targetFactory));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _languageService = languageService;
        if (_languageService is not null)
        {
            _languageService.LanguageChanged += OnLanguageChanged;
        }

        RefreshLocalizedState();
    }

    public ObservableCollection<LauncherVersionComponentItem> Components { get; } = [];

    public Func<LauncherVersionChangeConfirmationRequest, Task<bool>> ConfirmVersionChangeAsync { get; set; }
        = _ => Task.FromResult(false);

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
        await CheckAsync([profile]).ConfigureAwait(true);
    }

    public async Task CheckAsync(IReadOnlyList<LauncherProfileDefinition> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count == 0)
        {
            Reset();
            SetStatus("Launcher_ClientRelease_StatusNoProfile");
            return;
        }

        var profilesSnapshot = profiles
            .Where(static profile => profile is not null)
            .DistinctBy(static profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _activeProfiles = profilesSnapshot;
        _activeProfile = profilesSnapshot[0];
        IsVisible = true;
        IsProgressVisible = false;
        Progress = 0;
        DetailText = string.Empty;
        SetStatus("Launcher_ClientRelease_StatusChecking", _activeProfile.DisplayName);
        IsBusy = true;

        try
        {
            var results = new List<ProfileReleaseCheckResult>();
            foreach (var profile in profilesSnapshot)
            {
                var result = await _clientReleaseService
                    .CheckReleaseCatalogAsync(_targetFactory.Create(profile))
                    .ConfigureAwait(true);
                results.Add(new ProfileReleaseCheckResult(profile, result));
            }

            ApplyCheckResults(results);
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

    public async Task ApplyVersionAsync(LauncherVersionOptionItem option)
    {
        ArgumentNullException.ThrowIfNull(option);

        if (_activeProfiles.Count == 0)
        {
            SetStatus("Launcher_ClientRelease_StatusNoProfile");
            return;
        }

        if (!option.CanApply)
        {
            return;
        }

        if (_launchService.HasAnyRunningShellProcess())
        {
            IsProgressVisible = false;
            Progress = 0;
            SetStatus("Launcher_ClientRelease_StatusShellRunning");
            DetailText = LauncherText.Get(_languageService, "Launcher_ClientRelease_ShellRunningDetail");
            return;
        }

        if (option.RequiresConfirmation)
        {
            var confirmed = await ConfirmVersionChangeAsync(new LauncherVersionChangeConfirmationRequest(
                    option.ComponentKind,
                    option.DisplayName,
                    option.CurrentVersion,
                    option.Version,
                    option.Status))
                .ConfigureAwait(true);
            if (!confirmed)
            {
                SetStatus("Launcher_ClientRelease_StatusCanceled");
                return;
            }
        }

        IsProgressVisible = true;
        Progress = 0;
        DetailText = string.Empty;
        SetStatus("Launcher_ClientRelease_StatusApplyingVersion", option.DisplayName, option.Version);
        IsBusy = true;

        try
        {
            var progress = new Progress<int>(value => Progress = value);
            if (option.ComponentKind == EdgeComponentKind.Host)
            {
                await ApplyHostVersionAsync(option, progress).ConfigureAwait(true);
                return;
            }

            await ApplyPluginVersionAsync(option, progress).ConfigureAwait(true);
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
                _ = await _clientReleaseService.ReportCurrentVersionsAsync(_targetFactory.Create(profile)).ConfigureAwait(false);
            }
            catch
            {
                // 版本上报是非阻断链路，失败不能影响 Launcher 登录或 Shell 启动。
            }
        }
    }

    public void ToggleComponent(LauncherVersionComponentItem component)
    {
        ArgumentNullException.ThrowIfNull(component);
        component.ToggleExpanded(
            LauncherText.Get(_languageService, "Launcher_VersionManagement_ButtonExpand"),
            LauncherText.Get(_languageService, "Launcher_VersionManagement_ButtonCollapse"));
    }

    public void Reset()
    {
        Components.Clear();
        OnPropertyChanged(nameof(Components));
        _activeProfile = null;
        _activeProfiles = [];
        _profileByModuleId.Clear();
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

    private async Task ApplyPluginVersionAsync(LauncherVersionOptionItem option, IProgress<int> progress)
    {
        var profile = ResolveProfileForVersionOption(option);
        if (profile is null)
        {
            SetStatus("Launcher_ClientRelease_StatusNoProfile");
            return;
        }

        var result = await _clientReleaseService
            .ApplyPluginVersionAsync(_targetFactory.Create(profile), option.ModuleId, option.Version, progress)
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
        await CheckAsync(_activeProfiles).ConfigureAwait(true);
    }

    private async Task ApplyHostVersionAsync(LauncherVersionOptionItem option, IProgress<int> progress)
    {
        var profile = ResolveProfileForVersionOption(option);
        if (profile is null)
        {
            SetStatus("Launcher_ClientRelease_StatusNoProfile");
            return;
        }

        var result = await _clientReleaseService
            .ApplyHostVersionAsync(_targetFactory.Create(profile), option.Version, progress)
            .ConfigureAwait(true);
        if (!result.Started)
        {
            SetStatus("Launcher_ClientRelease_StatusFailed");
            DetailText = LauncherText.Compact(result.ErrorMessage);
            return;
        }

        SetStatus("Launcher_ClientRelease_StatusHostApplyStarted", option.Version);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLocalizedState();
        foreach (var component in Components)
        {
            component.RefreshTexts(
                ResolveComponentKindText(component.ComponentKind),
                LauncherText.Get(_languageService, "Launcher_VersionManagement_ButtonExpand"),
                LauncherText.Get(_languageService, "Launcher_VersionManagement_ButtonCollapse"));
            foreach (var option in component.Versions)
            {
                option.StatusText = ResolveVersionStatusText(option.Status);
                option.ActionText = ResolveVersionActionText(option.Status, option.CurrentVersion, option.Version);
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

    private LauncherProfileDefinition? ResolveProfileForVersionOption(LauncherVersionOptionItem option)
    {
        if (option.ComponentKind == EdgeComponentKind.Host)
        {
            return _activeProfile ?? _activeProfiles.FirstOrDefault();
        }

        return _profileByModuleId.TryGetValue(option.ModuleId, out var profile)
            ? profile
            : _activeProfiles.FirstOrDefault();
    }

    private void ApplyCheckResult(EdgeReleaseCatalogResult result)
    {
        if (_activeProfile is null)
        {
            Reset();
            SetStatus("Launcher_ClientRelease_StatusNoProfile");
            return;
        }

        ApplyCheckResults([new ProfileReleaseCheckResult(_activeProfile, result)]);
    }

    private void ApplyCheckResults(IReadOnlyList<ProfileReleaseCheckResult> results)
    {
        Components.Clear();
        _profileByModuleId.Clear();

        var orderedPlans = BuildAggregatedPlans(results);
        foreach (var plan in orderedPlans)
        {
            var versions = plan.Versions
                .Select(option => BuildVersionOption(plan, option))
                .ToArray();

            Components.Add(new LauncherVersionComponentItem(
                plan.ComponentKind,
                plan.ModuleId,
                string.IsNullOrWhiteSpace(plan.DisplayName) ? plan.ModuleId : plan.DisplayName,
                plan.CurrentVersion ?? LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_NotInstalled"),
                ResolveComponentKindText(plan.ComponentKind),
                LauncherText.Get(_languageService, "Launcher_VersionManagement_ButtonExpand"),
                versions));
        }
        OnPropertyChanged(nameof(Components));

        var statusResult = ResolveStatusResult(results);
        DetailText = LauncherText.Compact(string.Join(
            Environment.NewLine,
            results
                .Select(static result => result.Result.ErrorMessage)
                .Where(static message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.OrdinalIgnoreCase)));
        switch (statusResult.State)
        {
            case EdgeReleaseCatalogState.Succeeded:
                SetStatus(
                    "Launcher_ClientRelease_StatusReady",
                    statusResult.HostVersion,
                    ResolveHostTargetVersion(statusResult),
                    orderedPlans.Count(static component => component.ComponentKind == EdgeComponentKind.Plugin));
                break;
            case EdgeReleaseCatalogState.NotConfigured:
                SetStatus("Launcher_ClientRelease_StatusNotConfigured");
                break;
            case EdgeReleaseCatalogState.BootstrapFailed:
                SetStatus("Launcher_ClientRelease_StatusBootstrapFailed");
                break;
            case EdgeReleaseCatalogState.CatalogUnavailable:
                SetStatus("Launcher_ClientRelease_StatusCatalogFailed");
                break;
            default:
                SetStatus("Launcher_ClientRelease_StatusFailed");
                break;
        }
    }

    private IReadOnlyList<EdgeComponentVersionPlan> BuildAggregatedPlans(
        IReadOnlyList<ProfileReleaseCheckResult> results)
    {
        var plans = new List<EdgeComponentVersionPlan>();
        var plannedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in results)
        {
            foreach (var plan in item.Result.Components
                         .OrderBy(static component => component.ComponentKind == EdgeComponentKind.Host ? 0 : 1)
                         .ThenBy(static component => component.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                if (plan.ComponentKind == EdgeComponentKind.Host)
                {
                    if (plans.Any(static component => component.ComponentKind == EdgeComponentKind.Host))
                    {
                        continue;
                    }

                    plans.Add(plan);
                    continue;
                }

                if (!plannedModules.Add(plan.ModuleId))
                {
                    continue;
                }

                _profileByModuleId[plan.ModuleId] = item.Profile;
                plans.Add(plan);
            }
        }

        return plans
            .OrderBy(static component => component.ComponentKind == EdgeComponentKind.Host ? 0 : 1)
            .ThenBy(static component => component.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static EdgeReleaseCatalogResult ResolveStatusResult(IReadOnlyList<ProfileReleaseCheckResult> results)
    {
        var preferred = results.FirstOrDefault(static item => item.Result.State == EdgeReleaseCatalogState.Succeeded)
                        ?? results.FirstOrDefault();
        return preferred?.Result
               ?? new EdgeReleaseCatalogResult(
                   EdgeReleaseCatalogState.NotConfigured,
                   string.Empty,
                   string.Empty,
                   string.Empty,
                   string.Empty,
                   [],
                   "未选择工序。");
    }

    private LauncherVersionOptionItem BuildVersionOption(EdgeComponentVersionPlan plan, EdgeVersionOption option)
    {
        var releaseNotes = option.PluginRelease?.ReleaseNotes
                           ?? option.HostRelease?.Version.ReleaseNotes
                           ?? string.Empty;
        var packageSize = option.PluginRelease?.PackageSize
                          ?? option.HostRelease?.Version.PackageSize
                          ?? 0;
        var publishedAtUtc = option.PluginRelease?.Version.PublishedAtUtc
                             ?? option.PluginRelease?.Version.CreatedAtUtc
                             ?? option.HostRelease?.Version.PublishedAtUtc
                             ?? option.HostRelease?.Version.CreatedAtUtc;
        var currentVersion = plan.CurrentVersion ?? LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_NotInstalled");

        return new LauncherVersionOptionItem(
            plan.ComponentKind,
            plan.ModuleId,
            string.IsNullOrWhiteSpace(plan.DisplayName) ? plan.ModuleId : plan.DisplayName,
            currentVersion,
            option.Version,
            option.Status,
            option.CanApply,
            option.CompatibilityIssue ?? string.Empty,
            FormatPackageSize(packageSize),
            publishedAtUtc,
            releaseNotes,
            ResolveVersionStatusKind(option.Status),
            ResolveVersionStatusText(option.Status),
            ResolveVersionActionKind(option.Status),
            ResolveVersionActionText(option.Status, currentVersion, option.Version));
    }

    private static string ResolveHostTargetVersion(EdgeReleaseCatalogResult result)
        => result.Components
               .FirstOrDefault(static component => component.ComponentKind == EdgeComponentKind.Host)?
               .Versions.FirstOrDefault()?.Version
           ?? result.HostVersion;

    private string ResolveComponentKindText(EdgeComponentKind componentKind)
        => componentKind == EdgeComponentKind.Host
            ? LauncherText.Get(_languageService, "Launcher_VersionManagement_ComponentHost")
            : LauncherText.Get(_languageService, "Launcher_VersionManagement_ComponentPlugin");

    private string ResolveVersionStatusText(EdgeVersionStatus state)
        => state switch
        {
            EdgeVersionStatus.NotInstalled => LauncherText.Get(_languageService, "Launcher_VersionManagement_StatusNotInstalled"),
            EdgeVersionStatus.Newer => LauncherText.Get(_languageService, "Launcher_VersionManagement_StatusNewer"),
            EdgeVersionStatus.Current => LauncherText.Get(_languageService, "Launcher_VersionManagement_StatusCurrent"),
            EdgeVersionStatus.InstalledNewer => LauncherText.Get(_languageService, "Launcher_VersionManagement_StatusInstalledNewer"),
            EdgeVersionStatus.Incompatible => LauncherText.Get(_languageService, "Launcher_VersionManagement_StatusIncompatible"),
            EdgeVersionStatus.Deprecated => LauncherText.Get(_languageService, "Launcher_VersionManagement_StatusDeprecated"),
            EdgeVersionStatus.Older => LauncherText.Get(_languageService, "Launcher_VersionManagement_StatusOlder"),
            _ => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusUnknown")
        };

    private string ResolveVersionActionText(EdgeVersionStatus state, string currentVersion, string targetVersion)
        => state switch
        {
            EdgeVersionStatus.NotInstalled => LauncherText.Get(_languageService, "Launcher_VersionManagement_ButtonInstall"),
            EdgeVersionStatus.Newer => LauncherText.Get(_languageService, "Launcher_VersionManagement_ButtonUpdate"),
            EdgeVersionStatus.Older => LauncherText.Get(_languageService, "Launcher_VersionManagement_ButtonRollback"),
            EdgeVersionStatus.Deprecated when IsOlderVersion(currentVersion, targetVersion) => LauncherText.Get(_languageService, "Launcher_VersionManagement_ButtonRollback"),
            EdgeVersionStatus.Deprecated => LauncherText.Get(_languageService, "Launcher_VersionManagement_ButtonInstall"),
            _ => LauncherText.Get(_languageService, "Launcher_VersionManagement_ButtonUnavailable")
        };

    private static string ResolveVersionActionKind(EdgeVersionStatus state)
        => state switch
        {
            EdgeVersionStatus.Older or EdgeVersionStatus.Deprecated => "Danger",
            EdgeVersionStatus.NotInstalled or EdgeVersionStatus.Newer => "Secondary",
            _ => "Ghost"
        };

    private static string ResolveVersionStatusKind(EdgeVersionStatus state)
        => state switch
        {
            EdgeVersionStatus.Current => "Running",
            EdgeVersionStatus.Newer or EdgeVersionStatus.Older or EdgeVersionStatus.Deprecated => "Warning",
            EdgeVersionStatus.Incompatible => "Error",
            EdgeVersionStatus.NotInstalled or EdgeVersionStatus.InstalledNewer => "Info",
            _ => "Default"
        };

    private static bool IsOlderVersion(string currentVersion, string targetVersion)
    {
        if (Version.TryParse(currentVersion, out var current) &&
            Version.TryParse(targetVersion, out var target))
        {
            return target < current;
        }

        return string.Compare(targetVersion, currentVersion, StringComparison.OrdinalIgnoreCase) < 0;
    }

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

    private sealed record ProfileReleaseCheckResult(
        LauncherProfileDefinition Profile,
        EdgeReleaseCatalogResult Result);
}
