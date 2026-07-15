using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using Xunit;

namespace IIoT.Edge.Installer.UiTests;

public sealed class InstallerWindowHeadlessTests
{
    [AvaloniaFact]
    public void InstallerWindow_LoadsSharedProgressBarWithTheExistingInitialPercent()
    {
        var window = CreateWindow();

        try
        {
            window.Show();

            var welcomePage = window.FindControl<Grid>("WelcomePage");
            var installingPage = window.FindControl<Grid>("InstallingPage");
            var progressBar = window.FindControl<EdgeProgressBar>("InstallProgressBar");
            var percentText = window.FindControl<TextBlock>("ProgressPercentText");

            Assert.NotNull(welcomePage);
            Assert.True(welcomePage.IsVisible);
            Assert.NotNull(installingPage);
            Assert.False(installingPage.IsVisible);
            Assert.NotNull(progressBar);
            Assert.False(progressBar.IsIndeterminate);
            Assert.Equal(0d, progressBar.Value);
            Assert.NotNull(percentText);
            Assert.Equal("0%", percentText.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InstallerWindow_CloseButtonStaysInsideTheWindowBounds()
    {
        var window = CreateWindow();

        try
        {
            window.Show();
            window.UpdateLayout();

            var closeButton = window
                .GetVisualDescendants()
                .OfType<EdgeWindowButton>()
                .Single(button => button.Action == EdgeWindowButtonAction.Close);
            var origin = closeButton.TranslatePoint(default, window);

            Assert.NotNull(origin);
            Assert.True(origin.Value.X >= 0d);
            Assert.True(origin.Value.Y >= 0d);
            Assert.True(origin.Value.X + closeButton.Bounds.Width <= window.Bounds.Width);
            Assert.True(origin.Value.Y + closeButton.Bounds.Height <= window.Bounds.Height);
        }
        finally
        {
            window.Close();
        }
    }

    private static InstallerWindow CreateWindow()
    {
        var installRoot = Path.Combine(
            Path.GetTempPath(),
            "iiot-installer-window-tests",
            Guid.NewGuid().ToString("N"));
        return new InstallerWindow(new InstallerOptions(installRoot, false, false));
    }
}
