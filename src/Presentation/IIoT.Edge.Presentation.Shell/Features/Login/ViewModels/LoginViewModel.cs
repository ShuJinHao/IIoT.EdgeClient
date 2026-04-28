using System.Windows.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.Presentation.Shell.Features.Login;

public class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly IDeviceService _deviceService;
    private readonly IAppLanguageService _languageService;
    private string _employeeNo = string.Empty;
    private string _password = string.Empty;
    private bool _isLocalMode;
    private bool _isBusy;
    private string _errorMessage = string.Empty;

    public override string ViewId => "Core.Login";
    public override string ViewTitle => _languageService.GetString("Shell_ViewTitle_Login", "登录");

    public string EmployeeNo
    {
        get => _employeeNo;
        set
        {
            _employeeNo = value;
            OnPropertyChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            _password = value;
            OnPropertyChanged();
        }
    }

    public bool IsLocalMode
    {
        get => _isLocalMode;
        set
        {
            _isLocalMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCloudMode));
            OnPropertyChanged(nameof(ModeTitle));
            EmployeeNo = string.Empty;
            Password = string.Empty;
            ErrorMessage = string.Empty;
        }
    }

    public bool IsCloudMode => !_isLocalMode;

    public string ModeTitle => _isLocalMode
        ? _languageService.GetString("Shell_LocalAdmin", "本地紧急管理员")
        : _languageService.GetString("Shell_CloudLogin", "云端账号登录");

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public ICommand LoginCommand { get; }
    public ICommand SwitchModeCommand { get; }
    public ICommand CloseCommand { get; }

    public event Action? LoginSucceeded;

    public LoginViewModel(
        IAuthService authService,
        IDeviceService deviceService,
        IAppLanguageService languageService)
    {
        _authService = authService;
        _deviceService = deviceService;
        _languageService = languageService;

        LoginCommand = new AsyncCommand(ExecuteLoginAsync);
        SwitchModeCommand = new BaseCommand(_ => IsLocalMode = !IsLocalMode);
        CloseCommand = new BaseCommand(_ =>
        {
            EmployeeNo = string.Empty;
            Password = string.Empty;
            ErrorMessage = string.Empty;
        });

        _languageService.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ModeTitle));
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ErrorMessage = string.Empty;
            }
        };
    }

    private async Task ExecuteLoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = string.Empty;
        var trimmedEmployeeNo = EmployeeNo.Trim();
        var trimmedPassword = Password.Trim();

        if (string.IsNullOrWhiteSpace(trimmedPassword))
        {
            ErrorMessage = _languageService.GetString("Shell_PasswordRequired", "密码不能为空。");
            return;
        }

        if (IsCloudMode && string.IsNullOrWhiteSpace(trimmedEmployeeNo))
        {
            ErrorMessage = _languageService.GetString("Shell_EmployeeNoRequired", "工号不能为空。");
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
                    ErrorMessage = _languageService.GetString("Shell_DeviceCloudIdentityNotReady", "设备云端身份尚未就绪。");
                    return;
                }

                result = await _authService.LoginCloudAsync(trimmedEmployeeNo, trimmedPassword, deviceId.Value);
            }

            if (result.Success)
            {
                EmployeeNo = string.Empty;
                Password = string.Empty;
                LoginSucceeded?.Invoke();
            }
            else
            {
                EmployeeNo = trimmedEmployeeNo;
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
