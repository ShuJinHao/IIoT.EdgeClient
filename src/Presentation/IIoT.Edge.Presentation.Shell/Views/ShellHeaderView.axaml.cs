using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using IIoT.Edge.Presentation.Shell.Services;

namespace IIoT.Edge.Presentation.Shell.Views;

public partial class ShellHeaderView : UserControl
{
    public ShellHeaderView()
    {
        InitializeComponent();
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || IsHeaderControl(e.Source as AvaloniaObject))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            e.Handled = true;
            return;
        }

        if (GetWindow() is Window window)
        {
            window.BeginMoveDrag(e);
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        if (GetWindow() is Window window)
        {
            window.WindowState = WindowState.Minimized;
        }

        e.Handled = true;
    }

    private void OnAccountChipClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not IShellAuthContext authContext
            || sender is not Control target
            || GetWindow() is not Window owner)
        {
            return;
        }

        var flyout = new Flyout();
        flyout.Content = authContext.IsAuthenticated
            ? CreateAuthenticatedAccountMenu(authContext, flyout)
            : CreateLoginChoiceMenu(authContext, owner, flyout);
        flyout.ShowAt(target);
    }

    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
        e.Handled = true;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        GetWindow()?.Close();
        e.Handled = true;
    }

    private void ToggleMaximizeRestore()
    {
        if (GetWindow() is not Window window)
        {
            return;
        }

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private Window? GetWindow()
        => TopLevel.GetTopLevel(this) as Window;

    private static Control CreateLoginChoiceMenu(
        IShellAuthContext authContext,
        Window owner,
        Flyout flyout)
    {
        var (shell, panel) = CreateAccountMenuShell();
        panel.Children.Add(CreateMenuTitle("账号登录"));
        panel.Children.Add(CreateMenuSubtitle("请选择真实登录方式"));

        var localButton = CreateMenuButton("本地紧急登录");
        localButton.Click += async (_, args) =>
        {
            args.Handled = true;
            flyout.Hide();
            var dialog = new ShellLocalEmergencyLoginDialog(authContext);
            await dialog.ShowDialog<bool>(owner);
        };

        var cloudButton = CreateMenuButton("云端员工登录");
        cloudButton.Click += async (_, args) =>
        {
            args.Handled = true;
            flyout.Hide();
            var dialog = new ShellCloudLoginDialog(authContext);
            await dialog.ShowDialog<bool>(owner);
        };

        panel.Children.Add(localButton);
        panel.Children.Add(cloudButton);
        return shell;
    }

    private static Control CreateAuthenticatedAccountMenu(
        IShellAuthContext authContext,
        Flyout flyout)
    {
        var (shell, panel) = CreateAccountMenuShell();
        var user = authContext.CurrentUser;
        panel.Children.Add(CreateMenuTitle(user?.DisplayName ?? "已登录账号"));
        panel.Children.Add(CreateMenuSubtitle(user?.EmployeeNo ?? (user?.IsLocalAdmin == true ? "本地紧急登录" : "--")));

        var logoutButton = CreateMenuButton("退出登录");
        logoutButton.Click += (_, args) =>
        {
            args.Handled = true;
            authContext.Logout();
            flyout.Hide();
        };

        panel.Children.Add(logoutButton);
        return shell;
    }

    private static (Border Shell, StackPanel Panel) CreateAccountMenuShell()
    {
        var panel = new StackPanel
        {
            Spacing = 10
        };

        var shell = new Border
        {
            Classes = { "shell-account-menu" },
            Child = panel
        };

        return (shell, panel);
    }

    private static TextBlock CreateMenuTitle(string text)
        => new()
        {
            Classes = { "shell-account-menu-title" },
            Text = text
        };

    private static TextBlock CreateMenuSubtitle(string text)
        => new()
        {
            Classes = { "shell-account-menu-subtitle" },
            Text = text
        };

    private static Button CreateMenuButton(string text)
        => new()
        {
            Classes = { "shell-account-action" },
            Content = text,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };

    private static bool IsHeaderControl(AvaloniaObject? source)
    {
        if (source is not Visual visualSource)
        {
            return false;
        }

        foreach (var visual in visualSource.GetSelfAndVisualAncestors())
        {
            if (visual is Button)
            {
                return true;
            }
        }

        return false;
    }
}
