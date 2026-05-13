namespace IIoT.Edge.UI.Avalonia.Services;

public sealed class AvaloniaDialogService : IAvaloniaDialogService
{
    public event EventHandler<AvaloniaDialogRequest>? DialogRequested;

    public Task ShowInfoAsync(string title, string message)
    {
        DialogRequested?.Invoke(this, new AvaloniaDialogRequest(title, message));
        return Task.CompletedTask;
    }
}
