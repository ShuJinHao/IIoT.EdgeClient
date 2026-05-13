using Avalonia.Threading;

namespace IIoT.Edge.UI.Avalonia.Services;

public sealed class AvaloniaDispatcherService : IAvaloniaDispatcherService
{
    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }

    public Task InvokeAsync(Action action)
    {
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}
