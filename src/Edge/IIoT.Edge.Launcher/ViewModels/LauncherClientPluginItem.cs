using IIoT.Edge.Application.Abstractions.Updates;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherClientPluginItem : BaseNotifyPropertyChanged
{
    private string _currentVersion;
    private string _statusText;
    private string _actionText;

    public LauncherClientPluginItem(
        string moduleId,
        string displayName,
        string currentVersion,
        string targetVersion,
        string packageSizeText,
        string detailText,
        bool canInstallOrUpdate,
        string statusKind,
        string statusText,
        string actionText,
        EdgeVersionStatus state,
        LauncherVersionOptionItem? versionOption = null)
    {
        ModuleId = moduleId;
        DisplayName = displayName;
        _currentVersion = currentVersion;
        TargetVersion = targetVersion;
        PackageSizeText = packageSizeText;
        DetailText = detailText;
        CanInstallOrUpdate = canInstallOrUpdate;
        StatusKind = statusKind;
        _statusText = statusText;
        _actionText = actionText;
        State = state;
        VersionOption = versionOption;
    }

    public string ModuleId { get; }

    public string DisplayName { get; }

    public string CurrentVersion
    {
        get => _currentVersion;
        set
        {
            if (_currentVersion == value)
            {
                return;
            }

            _currentVersion = value;
            OnPropertyChanged();
        }
    }

    public string TargetVersion { get; }

    public string PackageSizeText { get; }

    public string DetailText { get; }

    public bool CanInstallOrUpdate { get; }

    public string StatusKind { get; }

    public EdgeVersionStatus State { get; }

    public LauncherVersionOptionItem? VersionOption { get; }

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
