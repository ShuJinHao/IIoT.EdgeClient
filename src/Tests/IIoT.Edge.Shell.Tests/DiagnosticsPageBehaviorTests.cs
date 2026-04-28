using System.Windows;
using System.Windows.Controls;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class DiagnosticsPageBehaviorTests
{
    [Fact]
    public Task DiagnosticsPage_ShouldLoadInsideContentControl()
        => RunOnStaThreadAsync(() =>
        {
            var page = new DiagnosticsPage();
            var host = new ContentControl { Content = page };
            var window = new Window
            {
                Width = 1280,
                Height = 720,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = host
            };

            window.Show();
            window.UpdateLayout();

            Assert.Same(page, host.Content);

            window.Close();
            return Task.CompletedTask;
        });

    private static Task RunOnStaThreadAsync(Func<Task> testBody)
        => WpfTestDispatcher.RunAsync(testBody);
}
