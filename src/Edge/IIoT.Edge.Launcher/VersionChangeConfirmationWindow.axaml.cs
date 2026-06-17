using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using IIoT.Edge.Application.Abstractions.Updates;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Shared.Avalonia.Windowing;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Launcher;

public partial class VersionChangeConfirmationWindow : Window
{
    private const int WindowCornerRadius = 8;

    public VersionChangeConfirmationWindow()
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
    }

    public VersionChangeConfirmationWindow(
        LauncherVersionChangeConfirmationRequest request,
        IAppLanguageService languageService)
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
        DataContext = new VersionChangeConfirmationViewModel(request, languageService);
    }

    private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void CloseWindowButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private sealed class VersionChangeConfirmationViewModel
    {
        public VersionChangeConfirmationViewModel(
            LauncherVersionChangeConfirmationRequest request,
            IAppLanguageService languageService)
        {
            var messageKey = request.Status == EdgeVersionStatus.Deprecated
                ? "Launcher_VersionManagement_ConfirmDeprecatedMessage"
                : "Launcher_VersionManagement_ConfirmRollbackMessage";
            var titleKey = request.Status == EdgeVersionStatus.Deprecated
                ? "Launcher_VersionManagement_ConfirmDeprecatedTitle"
                : "Launcher_VersionManagement_ConfirmRollbackTitle";

            Badge = LauncherText.Get(languageService, "Launcher_VersionManagement_ConfirmBadge");
            Title = LauncherText.Get(languageService, titleKey);
            Message = LauncherText.Format(
                languageService,
                messageKey,
                request.DisplayName,
                request.CurrentVersion,
                request.TargetVersion);
        }

        public string Badge { get; }

        public string Title { get; }

        public string Message { get; }
    }
}
