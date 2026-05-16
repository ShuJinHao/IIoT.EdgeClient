using IIoT.Edge.Launcher.Services;
using IIoT.Edge.UI.Avalonia.Localization;

namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherLoginViewModel : ObservableObject
{
    private readonly ILocalLauncherAuthService _authService;
    private readonly IAvaloniaLanguageService _languageService;

    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private string _statusMessage;
    private bool _isBusy;

    public LauncherLoginViewModel(
        ILocalLauncherAuthService authService,
        IAvaloniaLanguageService languageService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));

        _statusMessage = Text("Launcher_Status_PleaseLogin");
        LoginCommand = new AsyncLauncherCommand(() => LoginAsync(UserName, Password), () => !IsBusy);
        OpenChangePasswordCommand = new LauncherCommand(
            () => ChangePasswordRequested?.Invoke(this, EventArgs.Empty),
            () => !IsBusy);
    }

    public event EventHandler<LauncherLoginSucceededEventArgs>? LoginSucceeded;

    public event EventHandler? ChangePasswordRequested;

    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

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

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                LoginCommand.RaiseCanExecuteChanged();
                OpenChangePasswordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncLauncherCommand LoginCommand { get; }

    public LauncherCommand OpenChangePasswordCommand { get; }

    public async Task<bool> LoginAsync(string? userName, string? password)
    {
        UserName = userName?.Trim() ?? string.Empty;
        Password = password ?? string.Empty;
        ErrorMessage = string.Empty;
        StatusMessage = Text("Launcher_Status_Verifying");
        IsBusy = true;

        try
        {
            await Task.Yield();

            var result = _authService.Authenticate(UserName, Password);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? Text("Launcher_Error_LoginFailed");
                StatusMessage = Text("Launcher_Status_RetryAccount");
                return false;
            }

            StatusMessage = Text("Launcher_Status_SelectProfile");
            LoginSucceeded?.Invoke(this, new LauncherLoginSucceededEventArgs(result.DisplayName ?? string.Empty));
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ChangePasswordAsync(string? userName, string? oldPassword, string? newPassword)
    {
        ErrorMessage = string.Empty;
        StatusMessage = Text("Launcher_Status_ChangingPassword");
        IsBusy = true;

        try
        {
            await Task.Yield();
            var result = _authService.ChangePassword(userName, oldPassword, newPassword);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? Text("Launcher_Error_PasswordChangeFailed");
                StatusMessage = Text("Launcher_Status_RetryPassword");
                return false;
            }

            StatusMessage = Text("Launcher_Status_PasswordChanged");
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = Text("Launcher_Status_PasswordChangeFailed");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Reset()
    {
        Password = string.Empty;
        ErrorMessage = string.Empty;
        StatusMessage = Text("Launcher_Status_PleaseLogin");
    }

    public void ShowProfileLoadFailure(string errorMessage)
    {
        ErrorMessage = errorMessage;
        StatusMessage = Text("Launcher_Status_ProfileLoadFailed");
    }

    private string Text(string key) => _languageService.GetText(key);
}
