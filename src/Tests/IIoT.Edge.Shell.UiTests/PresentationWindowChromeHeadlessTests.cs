using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using IIoT.Edge.Presentation.Panels.Features.Equipment;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using Xunit;

namespace IIoT.Edge.Shell.UiTests;

public sealed class PresentationWindowChromeHeadlessTests
{
    [AvaloniaFact]
    public void ProductionPlanWindow_ShouldMatchNativeRegionToVisibleRootCornerRadius()
    {
        var window = new ProductionPlanSelectionWindow();

        try
        {
            window.Show();

            var chrome = Assert.IsType<EdgeDialogChrome>(window.Content);
            var visibleBorder = chrome
                .GetVisualDescendants()
                .OfType<Border>()
                .First();
            var regionRadius = Assert.IsType<int>(typeof(ProductionPlanSelectionWindow)
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

    [AvaloniaFact]
    public void ShellCrashDialog_ShouldRequestTransparentCompositionAndPreserveOverlay()
    {
        var window = new ShellCrashDialog("真实崩溃信息");

        try
        {
            window.Show();

            Assert.Contains(
                WindowTransparencyLevel.Transparent,
                window.TransparencyLevelHint);
            var overlay = Assert.IsAssignableFrom<ISolidColorBrush>(window.Background);
            Assert.InRange(overlay.Color.A, (byte)1, (byte)254);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ProductionPlanWindow_ShouldBindExistingLocalizedErrorTitle()
    {
        const string expectedTitle = "existing-plan-error-title";
        var window = new ProductionPlanSelectionWindow();
        window.Resources["Panels_PlanDialog_ErrorTitle"] = expectedTitle;

        try
        {
            window.Show();

            var table = window
                .GetVisualDescendants()
                .OfType<EdgeTablePanel>()
                .Single();
            Assert.Equal(expectedTitle, table.ErrorTitle);
        }
        finally
        {
            window.Close();
        }
    }
}
