using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using IIoT.Edge.Presentation.Shell.Services;
using IIoT.Edge.UI.Shared.Avalonia.Controls;

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

    private async void OnAccountChipClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not IShellAuthContext authContext
            || sender is not Control target
            || GetWindow() is not Window owner)
        {
            return;
        }

        if (!authContext.IsAuthenticated)
        {
            var dialog = new ShellLoginDialog(authContext);
            dialog.PrepareForOwner(owner);
            await dialog.ShowDialog<bool>(owner);
            return;
        }

        var flyout = new Flyout();
        flyout.FlyoutPresenterClasses.Add("shell-account-flyout");
        flyout.Content = CreateAuthenticatedAccountMenu(authContext, flyout);
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

    private Control CreateAuthenticatedAccountMenu(
        IShellAuthContext authContext,
        Flyout flyout)
    {
        var (shell, panel) = CreateAccountMenuShell();
        var user = authContext.CurrentUser;
        panel.Children.Add(CreateMenuTitle(user?.DisplayName ?? Res("Shell_Login_LoggedInAs", string.Empty)));
        panel.Children.Add(CreateMenuSubtitle(user?.EmployeeNo ?? (user?.IsLocalAdmin == true ? Res("Shell_Login_LocalEmergency", string.Empty) : "--")));

        var logoutButton = CreateMenuButton(Res("Shell_Login_Logout", string.Empty));
        logoutButton.Click += (_, args) =>
        {
            args.Handled = true;
            authContext.Logout();
            flyout.Hide();
        };

        panel.Children.Add(logoutButton);
        return shell;
    }

    /// <summary>
    /// 从资源字典读取本地化字符串，找不到时返回 fallback。
    /// </summary>
    private string Res(string key, string fallback)
        => this.TryFindResource(key, out var value) && value is string text
            ? text
            : fallback;

    private static (EdgeCard Shell, StackPanel Panel) CreateAccountMenuShell()
    {
        var panel = new StackPanel
        {
            Spacing = 10
        };

        var shell = new EdgeCard
        {
            Elevation = EdgeCardElevation.Float,
            PaddingMode = EdgeCardPaddingMode.Compact,
            Surface = EdgeCardSurface.Card,
            Classes = { "shell-account-menu" },
            Content = panel
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
