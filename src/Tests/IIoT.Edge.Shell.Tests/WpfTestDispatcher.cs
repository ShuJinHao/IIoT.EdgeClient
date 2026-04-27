using System.Windows;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace IIoT.Edge.Shell.Tests;

internal static class WpfTestDispatcher
{
    private static readonly Lazy<Dispatcher> Dispatcher = new(StartDispatcher);

    public static Task RunAsync(Action testBody)
        => Dispatcher.Value.InvokeAsync(testBody).Task;

    public static Task RunAsync(Func<Task> testBody)
        => Dispatcher.Value.InvokeAsync(testBody).Task.Unwrap();

    public static void EnsureApplication()
    {
        if (WpfApplication.Current is null)
        {
            _ = new WpfApplication
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            return;
        }

        WpfApplication.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private static Dispatcher StartDispatcher()
    {
        var completion = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            EnsureApplication();
            completion.SetResult(dispatcher);
            System.Windows.Threading.Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return completion.Task.GetAwaiter().GetResult();
    }
}
