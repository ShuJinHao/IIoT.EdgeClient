namespace IIoT.Edge.UI.Avalonia.Services;

public enum AvaloniaDialogRequestKind
{
    Info,
    Confirm
}

public sealed class AvaloniaDialogRequest
{
    private readonly TaskCompletionSource<bool>? _completion;

    private AvaloniaDialogRequest(AvaloniaDialogRequestKind kind, string title, string message, bool requiresResult)
    {
        Kind = kind;
        Title = title;
        Message = message;
        _completion = requiresResult
            ? new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
    }

    public AvaloniaDialogRequest(string title, string message)
        : this(AvaloniaDialogRequestKind.Info, title, message, requiresResult: false)
    {
    }

    public AvaloniaDialogRequestKind Kind { get; }

    public string Title { get; }

    public string Message { get; }

    public Task<bool> Result => _completion?.Task ?? Task.FromResult(true);

    public bool IsCompleted => _completion?.Task.IsCompleted ?? true;

    public static AvaloniaDialogRequest CreateInfo(string title, string message)
        => new(AvaloniaDialogRequestKind.Info, title, message, requiresResult: false);

    public static AvaloniaDialogRequest CreateConfirm(string title, string message)
        => new(AvaloniaDialogRequestKind.Confirm, title, message, requiresResult: true);

    public void Complete(bool result)
    {
        _completion?.TrySetResult(result);
    }
}
