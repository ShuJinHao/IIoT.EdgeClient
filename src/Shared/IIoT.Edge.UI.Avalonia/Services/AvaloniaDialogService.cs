namespace IIoT.Edge.UI.Avalonia.Services;

public sealed class AvaloniaDialogService : IAvaloniaDialogService
{
    public event EventHandler<AvaloniaDialogRequest>? DialogRequested;

    public Task ShowInfoAsync(string title, string message)
    {
        DialogRequested?.Invoke(this, AvaloniaDialogRequest.CreateInfo(title, message));
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title, string message)
    {
        var handler = DialogRequested;
        if (handler is null)
        {
            return Task.FromResult(false);
        }

        var request = AvaloniaDialogRequest.CreateConfirm(title, message);
        handler.Invoke(this, request);
        return request.Result;
    }
}
