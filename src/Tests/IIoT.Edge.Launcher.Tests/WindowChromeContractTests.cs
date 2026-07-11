using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class WindowChromeContractTests
{
    [Fact]
    public void StartupErrorMessage_ShouldExcludeRawExceptionDetails()
    {
        const string secret = "token=secret-value;/Users/operator/private/config.json";
        var exception = new InvalidOperationException(secret);
        var message = App.CreateSafeStartupErrorMessage(exception);

        Assert.Contains(nameof(InvalidOperationException), message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", message, StringComparison.Ordinal);
        Assert.DoesNotContain("/Users/operator", message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void StartupErrorWindow_ShouldRenderFallbackTextThroughSharedDialogChrome()
    {
        var window = new LauncherStartupErrorWindow(
            "IIoT Edge Launcher",
            "启动失败",
            "初始化失败详情",
            "关闭");

        try
        {
            window.Show();

            var chrome = window.FindControl<EdgeDialogChrome>("StartupErrorChrome");
            Assert.NotNull(chrome);
            Assert.Equal("IIoT Edge Launcher", window.Title);
            Assert.Equal("启动失败", chrome.Title);
            Assert.Equal(
                "初始化失败详情",
                window.FindControl<TextBlock>("StartupErrorMessageText")?.Text);
            Assert.Equal(
                "关闭",
                window.FindControl<EdgeActionButton>("StartupErrorCloseButton")?.Content);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LauncherWindows_ShouldMatchNativeRegionToVisibleRootCornerRadius()
    {
        AssertRegionMatchesVisibleRoot(
            new LauncherStartupErrorWindow(
                "IIoT Edge Launcher",
                "启动失败",
                "本地启动器初始化失败：InvalidOperationException",
                "关闭"),
            "StartupErrorChrome");
        AssertRegionMatchesVisibleRoot(
            new VersionHistoryWindow(),
            "VersionHistoryWindowRoot");
        AssertRegionMatchesVisibleRoot(
            new ReleaseNotesWindow(),
            "ReleaseNotesWindowRoot");
    }

    private static void AssertRegionMatchesVisibleRoot(Window window, string rootName)
    {
        try
        {
            window.Show();

            var visibleRoot = window.FindControl<Control>(rootName);
            Assert.NotNull(visibleRoot);

            var visibleBorder = visibleRoot
                .GetVisualDescendants()
                .OfType<Border>()
                .First();
            var regionRadius = Assert.IsType<int>(window.GetType()
                .GetField("WindowCornerRadius", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetRawConstantValue());

            Assert.Equal(regionRadius, visibleBorder.CornerRadius.TopLeft);
            Assert.Equal(regionRadius, visibleBorder.CornerRadius.TopRight);
            Assert.Equal(regionRadius, visibleBorder.CornerRadius.BottomRight);
            Assert.Equal(regionRadius, visibleBorder.CornerRadius.BottomLeft);
        }
        finally
        {
            window.Close();
        }
    }
}
