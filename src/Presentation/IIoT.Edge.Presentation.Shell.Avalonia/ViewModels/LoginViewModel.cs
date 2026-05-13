using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Mvvm;

namespace IIoT.Edge.Presentation.Shell.Avalonia.ViewModels;

public sealed partial class LoginViewModel : AvaloniaViewModelBase
{
    private readonly IAuthService _authService;
    private readonly IDeviceService _deviceService;
    private readonly IAvaloniaLanguageService _languageService;

    public LoginViewModel(
        IAuthService authService,
        IDeviceService deviceService,
        IAvaloniaLanguageService languageService)
    {
        _authService = authService;
        _deviceService = deviceService;
        _languageService = languageService;
    }

    public override string ViewId => "Core.Login";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudMode))]
    [NotifyPropertyChangedFor(nameof(ModeTitle))]
    [NotifyPropertyChangedFor(nameof(SwitchModeText))]
    private bool isLocalMode;

    public bool IsCloudMode => !IsLocalMode;

    public string ModeTitle => IsLocalMode
        ? _languageService.GetText("Shell_Login_LocalMode")
        : _languageService.GetText("Shell_Login_CloudMode");

    public string SwitchModeText => IsLocalMode
        ? _languageService.GetText("Shell_Login_SwitchToCloud")
        : _languageService.GetText("Shell_Login_SwitchToLocal");

    [ObservableProperty]
    private string employeeNo = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string errorMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public event Action? LoginSucceeded;

    [RelayCommand]
    private void SwitchMode()
    {
        IsLocalMode = !IsLocalMode;
        EmployeeNo = string.Empty;
        Password = string.Empty;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void Clear()
    {
        EmployeeNo = string.Empty;
        Password = string.Empty;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = string.Empty;
        var trimmedPassword = Password.Trim();
        var trimmedEmployeeNo = EmployeeNo.Trim();

        if (string.IsNullOrWhiteSpace(trimmedPassword))
        {
            ErrorMessage = _languageService.GetText("Shell_Login_PasswordRequired");
            return;
        }

        if (IsCloudMode && string.IsNullOrWhiteSpace(trimmedEmployeeNo))
        {
            ErrorMessage = _languageService.GetText("Shell_Login_EmployeeNoRequired");
            return;
        }

        IsBusy = true;
        try
        {
            AuthResult result;
            if (IsLocalMode)
            {
                result = await _authService.LoginLocalAsync(trimmedPassword);
            }
            else
            {
                var deviceId = _deviceService.CurrentDevice?.DeviceId;
                if (!_deviceService.CanUploadToCloud || deviceId is null || deviceId == Guid.Empty)
                {
                    ErrorMessage = _languageService.GetText("Shell_Login_DeviceNotReady");
                    return;
                }

                result = await _authService.LoginCloudAsync(trimmedEmployeeNo, trimmedPassword, deviceId.Value);
            }

            if (result.Success)
            {
                Clear();
                LoginSucceeded?.Invoke();
            }
            else
            {
                ErrorMessage = result.Message;
            }
        }
        finally
        {
            Password = string.Empty;
            IsBusy = false;
        }
    }
}
