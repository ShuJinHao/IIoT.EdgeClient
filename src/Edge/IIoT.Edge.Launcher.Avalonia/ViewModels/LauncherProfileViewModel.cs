using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.UI.Avalonia.Localization;
using System.Collections.ObjectModel;
using System.Globalization;

namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherProfileViewModel : ObservableObject
{
    private readonly ILauncherProfileCatalog _profileCatalog;
    private readonly IShellLaunchService _launchService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly List<LauncherProfileDefinition> _allProfiles = [];

    private string _displayName = string.Empty;
    private string _errorMessage = string.Empty;
    private string _profileSearchText = string.Empty;
    private string _profileSummaryText;
    private string _statusMessage;
    private string _welcomeText;
    private bool _isBusy;

    public LauncherProfileViewModel(
        ILauncherProfileCatalog profileCatalog,
        IShellLaunchService launchService,
        IAvaloniaLanguageService languageService)
    {
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));

        _profileSummaryText = Text("Launcher_Profile_SummaryZero");
        _statusMessage = Text("Launcher_Status_SelectProfile");
        _welcomeText = Text("Launcher_Profile_LoggedOut");
        LaunchProfileCommand = new AsyncLauncherCommand(
            parameter => parameter switch
            {
                LauncherProfileGroupViewModel group => LaunchAsync(group.PrimaryProfile),
                LauncherProfileDefinition profile => LaunchAsync(profile),
                _ => Task.CompletedTask
            },
            parameter => !IsBusy && parameter is LauncherProfileGroupViewModel or LauncherProfileDefinition);
        LaunchVariantCommand = new AsyncLauncherCommand(
            parameter => parameter is LauncherProfileDefinition profile
                ? LaunchAsync(profile)
                : Task.CompletedTask,
            parameter => !IsBusy && parameter is LauncherProfileDefinition);
        BackToLoginCommand = new LauncherCommand(
            () => BackToLoginRequested?.Invoke(this, EventArgs.Empty),
            () => !IsBusy);
        RefreshProfilesCommand = new LauncherCommand(RefreshProfiles, () => !IsBusy);
    }

    public event EventHandler? BackToLoginRequested;

    public ObservableCollection<LauncherProfileDefinition> Profiles { get; } = [];

    public ObservableCollection<LauncherProfileGroupViewModel> ProfileGroups { get; } = [];

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                RaisePropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

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

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                LaunchProfileCommand.RaiseCanExecuteChanged();
                LaunchVariantCommand.RaiseCanExecuteChanged();
                BackToLoginCommand.RaiseCanExecuteChanged();
                RefreshProfilesCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncLauncherCommand LaunchProfileCommand { get; }

    public AsyncLauncherCommand LaunchVariantCommand { get; }

    public LauncherCommand BackToLoginCommand { get; }

    public LauncherCommand RefreshProfilesCommand { get; }

    public void Activate(string displayName)
    {
        _displayName = displayName;
        ErrorMessage = string.Empty;
        WelcomeText = FormatText("Launcher_Profile_Welcome", displayName);
        LoadProfiles();
        ProfileSearchText = string.Empty;
        ApplyProfileFilter();
        StatusMessage = Text("Launcher_Status_SelectProfile");
    }

    public Task LaunchAsync(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ErrorMessage = string.Empty;
        try
        {
            _launchService.Launch(profile);
            StatusMessage = FormatText("Launcher_Status_LaunchStarted", profile.DisplayName, profile.MachineProfile);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = FormatText("Launcher_Status_LaunchFailed", profile.DisplayName);
        }

        return Task.CompletedTask;
    }

    public void Reset()
    {
        _displayName = string.Empty;
        ErrorMessage = string.Empty;
        WelcomeText = Text("Launcher_Profile_LoggedOut");
        ProfileSearchText = string.Empty;
        _allProfiles.Clear();
        Profiles.Clear();
        ProfileGroups.Clear();
        ProfileSummaryText = Text("Launcher_Profile_SummaryZero");
        StatusMessage = Text("Launcher_Status_SelectProfile");
    }

    private void RefreshProfiles()
    {
        if (string.IsNullOrWhiteSpace(_displayName))
        {
            return;
        }

        try
        {
            ErrorMessage = string.Empty;
            LoadProfiles();
            ApplyProfileFilter();
            StatusMessage = Text("Launcher_Status_SelectProfile");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = Text("Launcher_Status_ProfileLoadFailed");
        }
    }

    private void LoadProfiles()
    {
        _allProfiles.Clear();
        _allProfiles.AddRange(_profileCatalog.LoadProfiles());
    }

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

        var filteredProfiles = filtered.ToList();
        var groups = BuildProfileGroups(filteredProfiles);

        Profiles.Clear();
        foreach (var profile in filteredProfiles)
        {
            Profiles.Add(profile);
        }

        ProfileGroups.Clear();
        foreach (var group in groups)
        {
            ProfileGroups.Add(group);
        }

        ProfileSummaryText = _allProfiles.Count == 0
            ? Text("Launcher_Profile_SummaryZero")
            : string.IsNullOrWhiteSpace(keyword)
                ? FormatText("Launcher_Profile_SummaryAll", BuildProfileGroups(_allProfiles).Count)
                : FormatText("Launcher_Profile_SummaryFiltered", ProfileGroups.Count, BuildProfileGroups(_allProfiles).Count);
    }

    private List<LauncherProfileGroupViewModel> BuildProfileGroups(IReadOnlyList<LauncherProfileDefinition> profiles)
    {
        var profileTitle = Text("Launcher_Profile_Title");
        var profileDescription = Text("Launcher_Profile_Description");
        return profiles
            .GroupBy(profile => profile.MachineProfile, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var variants = group
                    .OrderByDescending(profile => IsRuntimeProfile(profile))
                    .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var primary = variants[0];
                return new LauncherProfileGroupViewModel(
                    BuildGroupDisplayName(primary.DisplayName, profileTitle),
                    profileDescription,
                    group.Key,
                    primary,
                    variants);
            })
            .OrderBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildGroupDisplayName(string displayName, string profileTitle)
    {
        var avaloniaIndex = displayName.IndexOf("Avalonia", StringComparison.OrdinalIgnoreCase);
        var baseName = avaloniaIndex > 0 ? displayName[..avaloniaIndex].Trim() : displayName.Trim();
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = displayName.Trim();
        }

        return string.IsNullOrWhiteSpace(profileTitle)
            ? baseName
            : $"{baseName} {profileTitle}".Trim();
    }

    private static bool IsRuntimeProfile(LauncherProfileDefinition profile)
        => profile.Arguments?.Any(argument =>
            argument.Contains("--start-runtime", StringComparison.OrdinalIgnoreCase)) == true;

    private string Text(string key) => _languageService.GetText(key);

    private string FormatText(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Text(key), args);

    private static bool Contains(string? source, string keyword)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
