using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using IIoT.Edge.Launcher.ViewModels;

namespace IIoT.Edge.Launcher.Avalonia.Views;

public partial class ChangePasswordWindow : Window
{
    private readonly LauncherMainViewModel _viewModel;
    private readonly TextBox _userNameInput;
    private readonly TextBox _oldPasswordInput;
    private readonly TextBox _newPasswordInput;
    private readonly TextBox _confirmPasswordInput;
    private readonly TextBlock _dialogErrorText;

    public ChangePasswordWindow()
        : this(LauncherDesignTimeViewModelFactory.Create(), "101650")
    {
    }

    public ChangePasswordWindow(LauncherMainViewModel viewModel, string? initialUserName)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _userNameInput = this.FindControl<TextBox>("UserNameInput") ?? throw new InvalidOperationException(nameof(UserNameInput));
        _oldPasswordInput = this.FindControl<TextBox>("OldPasswordInput") ?? throw new InvalidOperationException(nameof(OldPasswordInput));
        _newPasswordInput = this.FindControl<TextBox>("NewPasswordInput") ?? throw new InvalidOperationException(nameof(NewPasswordInput));
        _confirmPasswordInput = this.FindControl<TextBox>("ConfirmPasswordInput") ?? throw new InvalidOperationException(nameof(ConfirmPasswordInput));
        _dialogErrorText = this.FindControl<TextBlock>("DialogErrorText") ?? throw new InvalidOperationException(nameof(DialogErrorText));
        _userNameInput.Text = initialUserName?.Trim() ?? string.Empty;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void ConfirmButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        _dialogErrorText.Text = string.Empty;
        if (!ValidateInput())
        {
            return;
        }

        var changed = await _viewModel.ChangePasswordAsync(
            _userNameInput.Text,
            _oldPasswordInput.Text,
            _newPasswordInput.Text);

        if (!changed)
        {
            _dialogErrorText.Text = _viewModel.ErrorMessage;
            return;
        }

        Close(true);
    }

    private void CancelButton_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button)
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(_userNameInput.Text))
        {
            _dialogErrorText.Text = _viewModel.GetText("Launcher_Validation_UserNameRequired");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_oldPasswordInput.Text))
        {
            _dialogErrorText.Text = _viewModel.GetText("Launcher_Validation_OldPasswordRequired");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_newPasswordInput.Text))
        {
            _dialogErrorText.Text = _viewModel.GetText("Launcher_Validation_NewPasswordRequired");
            return false;
        }

        if (_newPasswordInput.Text.Length < 6)
        {
            _dialogErrorText.Text = _viewModel.GetText("Launcher_Validation_NewPasswordMinLength");
            return false;
        }

        if (!string.Equals(_newPasswordInput.Text, _confirmPasswordInput.Text, StringComparison.Ordinal))
        {
            _dialogErrorText.Text = _viewModel.GetText("Launcher_Validation_ConfirmPasswordMismatch");
            return false;
        }

        return true;
    }
}
