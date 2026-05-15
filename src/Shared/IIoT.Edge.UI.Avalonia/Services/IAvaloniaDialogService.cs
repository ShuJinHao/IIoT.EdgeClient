namespace IIoT.Edge.UI.Avalonia.Services;

public interface IAvaloniaDialogService
{
    event EventHandler<AvaloniaDialogRequest>? DialogRequested;

    Task ShowInfoAsync(string title, string message);

    Task<bool> ConfirmAsync(string title, string message);
}
