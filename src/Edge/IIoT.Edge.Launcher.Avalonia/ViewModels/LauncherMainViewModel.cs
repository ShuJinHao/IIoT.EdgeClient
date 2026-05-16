using IIoT.Edge.Launcher.Models;
using IIoT.Edge.UI.Avalonia.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;

namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherMainViewModel : ObservableObject
{
    private readonly IAvaloniaLanguageService _languageService;
    private ObservableObject _currentView;
    private bool _isAuthenticated;

    public LauncherMainViewModel(
        LauncherLoginViewModel loginViewModel,
        LauncherProfileViewModel profileViewModel,
        IAvaloniaLanguageService languageService)
    {
        LoginViewModel = loginViewModel ?? throw new ArgumentNullException(nameof(loginViewModel));
        ProfileViewModel = profileViewModel ?? throw new ArgumentNullException(nameof(profileViewModel));
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        _currentView = LoginViewModel;

        AppVersionText = BuildAppVersionText();
        PlatformMetaText = Text("Launcher_Meta_Platform");
        MaintainerText = Text("Launcher_Meta_Maintainer");
        ArchitectureText = Text("Launcher_Meta_Architecture");

        LoginViewModel.LoginSucceeded += (_, args) => NavigateToProfile(args.DisplayName);
        ProfileViewModel.BackToLoginRequested += (_, _) => NavigateToLogin();
        LoginViewModel.PropertyChanged += HandleChildPropertyChanged;
        ProfileViewModel.PropertyChanged += HandleChildPropertyChanged;
    }

    public LauncherLoginViewModel LoginViewModel { get; }

    public LauncherProfileViewModel ProfileViewModel { get; }

    public ObservableObject CurrentView
    {
        get => _currentView;
        private set
        {
            if (SetProperty(ref _currentView, value))
            {
                RaiseFacadeProperties();
            }
        }
    }

    public string ErrorMessage => IsProfileCurrent
        ? ProfileViewModel.ErrorMessage
        : LoginViewModel.ErrorMessage;

    public bool HasError => IsProfileCurrent
        ? ProfileViewModel.HasError
        : LoginViewModel.HasError;

    public string StatusMessage => IsProfileCurrent
        ? ProfileViewModel.StatusMessage
        : LoginViewModel.StatusMessage;

    public string WelcomeText => ProfileViewModel.WelcomeText;

    public string ProfileSearchText
    {
        get => ProfileViewModel.ProfileSearchText;
        set => ProfileViewModel.ProfileSearchText = value;
    }

    public string ProfileSummaryText => ProfileViewModel.ProfileSummaryText;

    public ObservableCollection<LauncherProfileDefinition> Profiles => ProfileViewModel.Profiles;

    public ObservableCollection<LauncherProfileGroupViewModel> ProfileGroups => ProfileViewModel.ProfileGroups;

    public string AppVersionText { get; }

    public string PlatformMetaText { get; }

    public string MaintainerText { get; }

    public string ArchitectureText { get; }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set => SetProperty(ref _isAuthenticated, value);
    }

    public bool IsBusy => LoginViewModel.IsBusy || ProfileViewModel.IsBusy;

    private bool IsProfileCurrent => ReferenceEquals(CurrentView, ProfileViewModel);

    public string GetText(string key) => Text(key);

    public void NavigateToProfile(string displayName)
    {
        try
        {
            ProfileViewModel.Activate(displayName);
            IsAuthenticated = true;
            CurrentView = ProfileViewModel;
        }
        catch (Exception ex)
        {
            ProfileViewModel.Reset();
            IsAuthenticated = false;
            CurrentView = LoginViewModel;
            LoginViewModel.ShowProfileLoadFailure(ex.Message);
            RaiseFacadeProperties();
        }
    }

    public void NavigateToLogin()
    {
        ProfileViewModel.Reset();
        LoginViewModel.Reset();
        IsAuthenticated = false;
        CurrentView = LoginViewModel;
        RaiseFacadeProperties();
    }

    public async Task LoginAsync(string? userName, string? password)
    {
        await LoginViewModel.LoginAsync(userName, password);
        RaiseFacadeProperties();
    }

    public Task<bool> ChangePasswordAsync(string? userName, string? oldPassword, string? newPassword)
        => LoginViewModel.ChangePasswordAsync(userName, oldPassword, newPassword);

    public async Task LaunchAsync(LauncherProfileDefinition profile)
    {
        await ProfileViewModel.LaunchAsync(profile);
        RaiseFacadeProperties();
    }

    private void HandleChildPropertyChanged(object? sender, PropertyChangedEventArgs args)
        => RaiseFacadeProperties();

    private void RaiseFacadeProperties()
    {
        RaisePropertyChanged(nameof(ErrorMessage));
        RaisePropertyChanged(nameof(HasError));
        RaisePropertyChanged(nameof(StatusMessage));
        RaisePropertyChanged(nameof(WelcomeText));
        RaisePropertyChanged(nameof(ProfileSearchText));
        RaisePropertyChanged(nameof(ProfileSummaryText));
        RaisePropertyChanged(nameof(Profiles));
        RaisePropertyChanged(nameof(ProfileGroups));
        RaisePropertyChanged(nameof(IsBusy));
    }

    private string Text(string key) => _languageService.GetText(key);

    private static string BuildAppVersionText()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null)
        {
            return "v1.0.0";
        }

        return $"v{version.Major}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";
    }
}
