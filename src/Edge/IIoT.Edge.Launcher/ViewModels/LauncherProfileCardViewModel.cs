using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using IIoT.Edge.Application.Abstractions.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Launcher.ViewModels;

public enum LauncherProfileCardState
{
    Checking,
    Latest,
    UpdateAvailable,
    Running,
    Unregistered,
    Offline,
    Updating
}

public enum LauncherProfileCardActionKind
{
    None,
    Launch,
    ContactAdmin
}

public sealed class LauncherProfileCardViewModel : BaseNotifyPropertyChanged
{
    private readonly IAppLanguageService? _languageService;
    private LauncherProfileCardState _state;
    private LauncherProfileCardActionKind _actionKind;
    private string _statusKind = "Info";
    private string _statusText = string.Empty;
    private string _primaryActionText = string.Empty;
    private string _summaryText = string.Empty;
    private string _technicalDetailText = string.Empty;
    private int _progress;
    private bool _isProgressVisible;
    private bool _isPrimaryActionEnabled;

    public LauncherProfileCardViewModel(
        LauncherProfileDefinition profile,
        IAppLanguageService? languageService = null)
    {
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _languageService = languageService;
        SetChecking();
    }

    public LauncherProfileDefinition Profile { get; }

    public ObservableCollection<LauncherClientPluginItem> Plugins { get; } = [];

    public EdgeHostUpdateCheckResult? HostUpdateCheck { get; private set; }

    public EdgeReleaseCatalogResult? ClientReleaseCheck { get; private set; }

    public string ProfileId => Profile.ProfileId;

    public string DisplayName => Profile.DisplayName;

    public string Description => Profile.Description;

    public string MachineProfile => Profile.MachineProfile;

    public string IconKind => Profile.IconKind;

    public string PluginDisplayPath => Profile.PluginDisplayPath;

    public string DataDisplayPath => Profile.DataDisplayPath;

    public LauncherProfileCardState State
    {
        get => _state;
        private set => SetProperty(ref _state, value);
    }

    public LauncherProfileCardActionKind ActionKind
    {
        get => _actionKind;
        private set => SetProperty(ref _actionKind, value);
    }

    public string StatusKind
    {
        get => _statusKind;
        private set => SetProperty(ref _statusKind, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string PrimaryActionText
    {
        get => _primaryActionText;
        private set => SetProperty(ref _primaryActionText, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public string TechnicalDetailText
    {
        get => _technicalDetailText;
        private set
        {
            if (SetProperty(ref _technicalDetailText, LauncherText.Compact(value)))
            {
                OnPropertyChanged(nameof(HasDetail));
            }
        }
    }

    public int Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, Math.Clamp(value, 0, 100));
    }

    public bool IsProgressVisible
    {
        get => _isProgressVisible;
        private set => SetProperty(ref _isProgressVisible, value);
    }

    public bool IsPrimaryActionEnabled
    {
        get => _isPrimaryActionEnabled;
        private set => SetProperty(ref _isPrimaryActionEnabled, value);
    }

    public bool HasDetail => !string.IsNullOrWhiteSpace(TechnicalDetailText) || Plugins.Count > 0;

    public void SetChecking()
    {
        Plugins.Clear();
        HostUpdateCheck = null;
        ClientReleaseCheck = null;
        Progress = 0;
        IsProgressVisible = false;
        SetState(
            LauncherProfileCardState.Checking,
            "Info",
            "Launcher_ProfileCard_StatusChecking",
            "Launcher_ProfileCard_ActionChecking",
            LauncherProfileCardActionKind.None,
            false,
            "Launcher_ProfileCard_DetailChecking");
    }

    public void SetReady()
    {
        Plugins.Clear();
        HostUpdateCheck = null;
        ClientReleaseCheck = null;
        Progress = 0;
        IsProgressVisible = false;
        SetState(
            LauncherProfileCardState.Latest,
            "Running",
            "Launcher_ProfileCard_StatusLatest",
            "Launcher_ProfileCard_ActionLaunch",
            LauncherProfileCardActionKind.Launch,
            true,
            "Launcher_ProfileCard_DetailLatest");
    }

    public void ApplyCheckResult(
        EdgeHostUpdateCheckResult? hostUpdateCheck,
        EdgeReleaseCatalogResult clientReleaseCheck)
    {
        ArgumentNullException.ThrowIfNull(clientReleaseCheck);

        HostUpdateCheck = hostUpdateCheck;
        ClientReleaseCheck = clientReleaseCheck;
        Progress = 0;
        IsProgressVisible = false;
        RebuildPluginDetails(clientReleaseCheck);

        if (clientReleaseCheck.State == EdgeReleaseCatalogState.NotConfigured)
        {
            SetState(
                LauncherProfileCardState.Unregistered,
                "Error",
                "Launcher_ProfileCard_StatusUnregistered",
                "Launcher_ProfileCard_ActionContactAdmin",
                LauncherProfileCardActionKind.ContactAdmin,
                false,
                "Launcher_ProfileCard_DetailUnregistered",
                LauncherText.Compact(clientReleaseCheck.ErrorMessage));
            return;
        }

        if (!clientReleaseCheck.Success)
        {
            SetState(
                LauncherProfileCardState.Offline,
                "Offline",
                "Launcher_ProfileCard_StatusOffline",
                "Launcher_ProfileCard_ActionLaunchOffline",
                LauncherProfileCardActionKind.Launch,
                true,
                "Launcher_ProfileCard_DetailOffline",
                LauncherText.Compact(clientReleaseCheck.ErrorMessage));
            return;
        }

        var pluginComponents = clientReleaseCheck.Components
            .Where(static component => component.ComponentKind == EdgeComponentKind.Plugin)
            .ToArray();
        var pendingPluginCount = pluginComponents
            .Where(static plan => plan.Versions.Any(static option => option.CanApply))
            .Select(static plan => plan.ModuleId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var hasIncompatiblePlugin = pluginComponents.Any(static plan =>
            plan.Versions.Any(static option => option.Status is EdgeVersionStatus.Incompatible or EdgeVersionStatus.Deprecated));
        var hasHostUpdate = hostUpdateCheck?.HasUpdate == true;
        if (hasHostUpdate || pendingPluginCount > 0)
        {
            SetUpdateAvailableState(pendingPluginCount, hasHostUpdate);
            return;
        }

        if (hasIncompatiblePlugin)
        {
            SetState(
                LauncherProfileCardState.Offline,
                "Offline",
                "Launcher_ProfileCard_StatusOffline",
                "Launcher_ProfileCard_ActionLaunchOffline",
                LauncherProfileCardActionKind.Launch,
                true,
                "Launcher_ProfileCard_DetailIncompatible",
                BuildTechnicalDetail(clientReleaseCheck, hostUpdateCheck));
            return;
        }

        SetState(
            LauncherProfileCardState.Latest,
            "Running",
            "Launcher_ProfileCard_StatusLatest",
            "Launcher_ProfileCard_ActionLaunch",
            LauncherProfileCardActionKind.Launch,
            true,
            "Launcher_ProfileCard_DetailLatest",
            BuildTechnicalDetail(clientReleaseCheck, hostUpdateCheck));
    }

    public void SetRunning()
    {
        IsProgressVisible = false;
        Progress = 0;
        SetState(
            LauncherProfileCardState.Running,
            "Info",
            "Launcher_ProfileCard_StatusRunning",
            "Launcher_ProfileCard_ActionRunning",
            LauncherProfileCardActionKind.None,
            false,
            "Launcher_ProfileCard_DetailRunning");
    }

    public void SetUpdating(string detailKey)
    {
        IsProgressVisible = true;
        Progress = 0;
        SetState(
            LauncherProfileCardState.Updating,
            "Warning",
            "Launcher_ProfileCard_StatusUpdating",
            "Launcher_ProfileCard_ActionWorking",
            LauncherProfileCardActionKind.None,
            false,
            detailKey);
    }

    public void SetUpdateFailed(string? errorMessage)
    {
        IsProgressVisible = false;
        Progress = 0;
        SetState(
            LauncherProfileCardState.Offline,
            "Offline",
            "Launcher_ProfileCard_StatusOffline",
            "Launcher_ProfileCard_ActionLaunchOffline",
            LauncherProfileCardActionKind.Launch,
            true,
            "Launcher_ProfileCard_DetailUpdateFailed",
            LauncherText.Compact(errorMessage));
    }

    public void SetProgress(int value)
        => Progress = value;

    public void RefreshLocalizedState()
    {
        switch (State)
        {
            case LauncherProfileCardState.Checking:
                SetChecking();
                break;
            case LauncherProfileCardState.Latest:
            case LauncherProfileCardState.UpdateAvailable:
            case LauncherProfileCardState.Unregistered:
            case LauncherProfileCardState.Offline:
                if (ClientReleaseCheck is not null)
                {
                    ApplyCheckResult(HostUpdateCheck, ClientReleaseCheck);
                }
                else
                {
                    SetReady();
                }
                break;
            case LauncherProfileCardState.Running:
                SetRunning();
                break;
            case LauncherProfileCardState.Updating:
                SetUpdating("Launcher_ProfileCard_DetailUpdating");
                break;
        }
    }

    private void SetUpdateAvailableState(int pendingPluginCount, bool hasHostUpdate)
    {
        var summary = pendingPluginCount > 0 && hasHostUpdate
            ? LauncherText.Format(_languageService, "Launcher_ProfileCard_DetailUpdateBoth", pendingPluginCount)
            : pendingPluginCount > 0
                ? LauncherText.Format(_languageService, "Launcher_ProfileCard_DetailUpdatePlugins", pendingPluginCount)
                : LauncherText.Get(_languageService, "Launcher_ProfileCard_DetailUpdateHost");

        SetState(
            LauncherProfileCardState.UpdateAvailable,
            "Warning",
            "Launcher_ProfileCard_StatusUpdateAvailable",
            "Launcher_ProfileCard_ActionLaunch",
            LauncherProfileCardActionKind.Launch,
            true,
            summary,
            BuildTechnicalDetail(ClientReleaseCheck, HostUpdateCheck));
    }

    private void SetState(
        LauncherProfileCardState state,
        string statusKind,
        string statusKey,
        string actionKey,
        LauncherProfileCardActionKind actionKind,
        bool isPrimaryActionEnabled,
        string summaryKeyOrText,
        string? technicalDetailText = null)
    {
        State = state;
        StatusKind = statusKind;
        StatusText = LauncherText.Get(_languageService, statusKey);
        PrimaryActionText = LauncherText.Get(_languageService, actionKey);
        ActionKind = actionKind;
        IsPrimaryActionEnabled = isPrimaryActionEnabled;
        SummaryText = summaryKeyOrText.StartsWith("Launcher_", StringComparison.Ordinal)
            ? LauncherText.Get(_languageService, summaryKeyOrText)
            : summaryKeyOrText;
        TechnicalDetailText = technicalDetailText ?? string.Empty;
    }

    private void RebuildPluginDetails(EdgeReleaseCatalogResult result)
    {
        Plugins.Clear();
        foreach (var plan in result.Components.Where(static component => component.ComponentKind == EdgeComponentKind.Plugin))
        {
            var option = SelectDisplayOption(plan);
            if (option?.PluginRelease is null)
            {
                continue;
            }

            var release = option.PluginRelease;
            Plugins.Add(new LauncherClientPluginItem(
                plan.ModuleId,
                string.IsNullOrWhiteSpace(plan.DisplayName) ? plan.ModuleId : plan.DisplayName,
                plan.CurrentVersion ?? LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_NotInstalled"),
                option.Version,
                FormatPackageSize(release.PackageSize),
                option.CompatibilityIssue ?? release.ReleaseNotes ?? string.Empty,
                option.CanApply,
                ResolvePluginStateKind(option.Status),
                ResolvePluginStateText(option.Status),
                ResolvePluginActionText(option.Status),
                option.Status));
        }

        OnPropertyChanged(nameof(HasDetail));
    }

    private string BuildTechnicalDetail(
        EdgeReleaseCatalogResult? clientReleaseCheck,
        EdgeHostUpdateCheckResult? hostUpdateCheck)
    {
        var parts = new List<string>();
        if (clientReleaseCheck is not null)
        {
            parts.Add(LauncherText.Format(
                _languageService,
                "Launcher_ProfileCard_TechnicalHostFormat",
                clientReleaseCheck.HostVersion,
                ResolveHostTargetVersion(clientReleaseCheck)));
            if (!string.IsNullOrWhiteSpace(clientReleaseCheck.ErrorMessage))
            {
                parts.Add(LauncherText.Compact(clientReleaseCheck.ErrorMessage));
            }
        }

        if (hostUpdateCheck?.HasUpdate == true)
        {
            parts.Add(LauncherText.Format(
                _languageService,
                "Launcher_ProfileCard_TechnicalHostUpdateFormat",
                hostUpdateCheck.CurrentVersion ?? string.Empty,
                hostUpdateCheck.TargetVersion ?? string.Empty));
        }
        else if (hostUpdateCheck?.State == EdgeHostUpdateCheckState.Failed &&
                 !string.IsNullOrWhiteSpace(hostUpdateCheck.ErrorMessage))
        {
            parts.Add(LauncherText.Compact(hostUpdateCheck.ErrorMessage));
        }

        return string.Join(Environment.NewLine, parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static EdgeVersionOption? SelectDisplayOption(EdgeComponentVersionPlan plan)
        => plan.Versions.FirstOrDefault(static option => option.Status is EdgeVersionStatus.NotInstalled or EdgeVersionStatus.Newer)
           ?? plan.Versions.FirstOrDefault(static option => option.Status == EdgeVersionStatus.Current)
           ?? plan.Versions.FirstOrDefault(static option => option.CanApply)
           ?? plan.Versions.FirstOrDefault();

    private static string ResolveHostTargetVersion(EdgeReleaseCatalogResult result)
        => result.Components
               .FirstOrDefault(static component => component.ComponentKind == EdgeComponentKind.Host)?
               .Versions.FirstOrDefault()?.Version
           ?? result.HostVersion;

    private string ResolvePluginStateText(EdgeVersionStatus state)
        => state switch
        {
            EdgeVersionStatus.NotInstalled => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusNotInstalled"),
            EdgeVersionStatus.Newer => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusUpdateAvailable"),
            EdgeVersionStatus.Current => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusLatest"),
            EdgeVersionStatus.InstalledNewer => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusInstalledNewer"),
            EdgeVersionStatus.Incompatible => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusIncompatible"),
            EdgeVersionStatus.Deprecated => LauncherText.Get(_languageService, "Launcher_VersionManagement_StatusDeprecated"),
            EdgeVersionStatus.Older => LauncherText.Get(_languageService, "Launcher_VersionManagement_StatusOlder"),
            _ => LauncherText.Get(_languageService, "Launcher_ClientRelease_Plugin_StatusUnknown")
        };

    private string ResolvePluginActionText(EdgeVersionStatus state)
        => state switch
        {
            EdgeVersionStatus.NotInstalled => LauncherText.Get(_languageService, "Launcher_ClientRelease_ButtonInstall"),
            EdgeVersionStatus.Newer => LauncherText.Get(_languageService, "Launcher_ClientRelease_ButtonUpdate"),
            EdgeVersionStatus.Older => LauncherText.Get(_languageService, "Launcher_VersionManagement_ButtonRollback"),
            _ => LauncherText.Get(_languageService, "Launcher_ClientRelease_ButtonNoAction")
        };

    private static string ResolvePluginStateKind(EdgeVersionStatus state)
        => state switch
        {
            EdgeVersionStatus.Current => "Running",
            EdgeVersionStatus.Newer or EdgeVersionStatus.Older => "Warning",
            EdgeVersionStatus.Incompatible or EdgeVersionStatus.Deprecated => "Error",
            EdgeVersionStatus.NotInstalled => "Info",
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
