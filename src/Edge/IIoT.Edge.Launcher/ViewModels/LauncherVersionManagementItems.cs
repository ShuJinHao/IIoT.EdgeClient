using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Launcher.ViewModels;

public sealed record LauncherVersionChangeConfirmationRequest(
    EdgeComponentKind ComponentKind,
    string DisplayName,
    string CurrentVersion,
    string TargetVersion,
    EdgeVersionStatus Status,
    IReadOnlyDictionary<string, string>? RequiredPluginVersions = null);

public sealed class LauncherVersionComponentItem : BaseNotifyPropertyChanged
{
    private bool _isExpanded;
    private string _expandActionText;

    public LauncherVersionComponentItem(
        EdgeComponentKind componentKind,
        string moduleId,
        string displayName,
        string currentVersion,
        string componentKindText,
        string expandActionText,
        IReadOnlyList<LauncherVersionOptionItem> versions,
        bool isCatalogAvailable = true)
    {
        ComponentKind = componentKind;
        ModuleId = moduleId;
        DisplayName = displayName;
        CurrentVersion = currentVersion;
        ComponentKindText = componentKindText;
        _expandActionText = expandActionText;
        Versions = versions;
        IsCatalogAvailable = isCatalogAvailable;
    }

    public EdgeComponentKind ComponentKind { get; }

    public string ModuleId { get; }

    public string DisplayName { get; }

    public string CurrentVersion { get; }

    public string ComponentKindText { get; private set; }

    public IReadOnlyList<LauncherVersionOptionItem> Versions { get; }

    public bool IsCatalogAvailable { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public string ExpandActionText
    {
        get => _expandActionText;
        private set
        {
            if (_expandActionText == value)
            {
                return;
            }

            _expandActionText = value;
            OnPropertyChanged();
        }
    }

    public void ToggleExpanded(string expandText, string collapseText)
    {
        IsExpanded = !IsExpanded;
        RefreshTexts(ComponentKindText, expandText, collapseText);
    }

    public void RefreshTexts(string componentKindText, string expandText, string collapseText)
    {
        ComponentKindText = componentKindText;
        OnPropertyChanged(nameof(ComponentKindText));
        ExpandActionText = IsExpanded ? collapseText : expandText;
    }
}

public sealed class LauncherVersionHistoryViewModel(
    LauncherVersionComponentItem component,
    LauncherClientReleasePanelViewModel panel)
{
    public LauncherVersionComponentItem Component { get; } = component ?? throw new ArgumentNullException(nameof(component));

    public LauncherClientReleasePanelViewModel Panel { get; } = panel ?? throw new ArgumentNullException(nameof(panel));
}

public sealed class LauncherVersionOptionItem : BaseNotifyPropertyChanged
{
    private string _statusText;
    private string _actionText;

    public LauncherVersionOptionItem(
        EdgeComponentKind componentKind,
        string moduleId,
        string displayName,
        string currentVersion,
        string version,
        EdgeVersionStatus status,
        bool canApply,
        string compatibilityIssue,
        string packageSizeText,
        DateTime? publishedAtUtc,
        string releaseNotes,
        string statusKind,
        string statusText,
        string actionKind,
        string actionText,
        EdgeVersionSelection? requiredComposition = null)
    {
        ComponentKind = componentKind;
        ModuleId = moduleId;
        DisplayName = displayName;
        CurrentVersion = currentVersion;
        Version = version;
        Status = status;
        CanApply = canApply;
        CompatibilityIssue = compatibilityIssue;
        PackageSizeText = packageSizeText;
        PublishedAtUtc = publishedAtUtc;
        ReleaseNotes = releaseNotes;
        StatusKind = statusKind;
        _statusText = statusText;
        ActionKind = actionKind;
        _actionText = actionText;
        RequiredComposition = requiredComposition;
    }

    public EdgeComponentKind ComponentKind { get; }

    public string ModuleId { get; }

    public string DisplayName { get; }

    public string CurrentVersion { get; }

    public string Version { get; }

    public EdgeVersionStatus Status { get; }

    public bool CanApply { get; }

    public bool HasNoApplyAction => !CanApply;

    public bool RequiresConfirmation
        => Status is EdgeVersionStatus.Older or EdgeVersionStatus.Deprecated
           || RequiredComposition?.PluginVersions.Count > 0;

    public EdgeVersionSelection? RequiredComposition { get; }

    public string CompatibilityIssue { get; }

    public string PackageSizeText { get; }

    public DateTime? PublishedAtUtc { get; }

    public string PublishedAtText => PublishedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    public string ReleaseNotes { get; }

    public string StatusKind { get; }

    public string ActionKind { get; }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public string ActionText
    {
        get => _actionText;
        set
        {
            if (_actionText == value)
            {
                return;
            }

            _actionText = value;
            OnPropertyChanged();
        }
    }
}
