using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Mvvm;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Shell.Avalonia.ViewModels;

public sealed partial class HeaderViewModel : AvaloniaViewModelBase
{
    private readonly IAuthService _authService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IAvaloniaWindowService _windowService;

    public HeaderViewModel(
        IAuthService authService,
        IAvaloniaLanguageService languageService,
        IAvaloniaWindowService windowService)
    {
        _authService = authService;
        _languageService = languageService;
        _windowService = windowService;
        _authService.AuthStateChanged += _ => RefreshUser();
        _windowService.StateChanged += (_, _) => MaxRestoreIcon = _windowService.MaxRestoreIcon;
        RefreshUser();
        MaxRestoreIcon = _windowService.MaxRestoreIcon;
    }

    public override string ViewId => "Core.SystemHeader";

    [ObservableProperty]
    private string currentUser = string.Empty;

    [ObservableProperty]
    private string maxRestoreIcon = "WindowMaximize";

    [RelayCommand]
    private void Minimize() => _windowService.Minimize();

    [RelayCommand]
    private void ToggleMaximize()
    {
        _windowService.ToggleMaximize();
        MaxRestoreIcon = _windowService.MaxRestoreIcon;
    }

    [RelayCommand]
    private void Close() => _windowService.Close();

    private void RefreshUser()
    {
        CurrentUser = _authService.IsAuthenticated
            ? _authService.CurrentUser?.DisplayName ?? _authService.CurrentUser?.EmployeeNo ?? _languageService.GetText("Shell_Login_NotAuthenticated")
            : _languageService.GetText("Shell_Login_NotAuthenticated");
    }
}
