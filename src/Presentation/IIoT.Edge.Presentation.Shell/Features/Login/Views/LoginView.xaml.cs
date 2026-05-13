using MaterialDesignThemes.Wpf;
using System.Windows.Controls;

namespace IIoT.Edge.Presentation.Shell.Features.Login;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (e.NewValue is LoginViewModel vm)
            {
                PasswordBox.PasswordChanged += (_, _) =>
                    vm.Password = PasswordBox.Password;

                vm.LoginSucceeded += () =>
                    DialogHost.CloseDialogCommand.Execute(null, null);
            }
        };
    }
}

