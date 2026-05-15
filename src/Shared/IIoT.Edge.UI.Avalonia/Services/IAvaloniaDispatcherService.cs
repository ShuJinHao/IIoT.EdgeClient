namespace IIoT.Edge.UI.Avalonia.Services;

public interface IAvaloniaDispatcherService
{
    void Post(Action action);

    Task InvokeAsync(Action action);
}
