using Avalonia.Threading;

namespace IIoT.Edge.AvaloniaPoc.Services;

public sealed class AvaloniaDispatcherService : IDispatcherService
{
    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }
}
