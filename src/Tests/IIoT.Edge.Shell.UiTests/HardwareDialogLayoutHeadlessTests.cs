using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using Xunit;

namespace IIoT.Edge.Shell.UiTests;

public sealed class HardwareDialogLayoutHeadlessTests
{
    [AvaloniaFact]
    public void NetworkDeviceEditor_OnProductionViewport_ShouldKeepDialogBoundedAndBodyScrollable()
    {
        var page = new NetworkDevicePage();
        var window = new Window
        {
            Width = 1024,
            Height = 768,
            Content = page
        };

        try
        {
            window.Show();

            var overlay = page
                .GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("edge-dialog-overlay"));
            overlay.IsVisible = true;

            window.Measure(new Size(1024, 768));
            window.Arrange(new Rect(0, 0, 1024, 768));

            var dialog = Assert.IsType<EdgeDialogChrome>(
                page.FindControl<EdgeDialogChrome>("NetworkDeviceDialog"));
            var scrollHost = Assert.IsType<EdgeScrollHost>(
                page.FindControl<EdgeScrollHost>("NetworkDeviceDialogScrollHost"));

            Assert.InRange(dialog.Bounds.Height, 1, 680);
            Assert.InRange(scrollHost.Bounds.Height, 1, 480);
            Assert.True(scrollHost.Extent.Height >= scrollHost.Viewport.Height);
            Assert.True(dialog.Bounds.Bottom <= overlay.Bounds.Bottom);
        }
        finally
        {
            window.Close();
        }
    }
}
