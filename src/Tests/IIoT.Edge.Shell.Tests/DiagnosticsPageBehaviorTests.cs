using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await testBody();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return completion.Task;
    }
}
