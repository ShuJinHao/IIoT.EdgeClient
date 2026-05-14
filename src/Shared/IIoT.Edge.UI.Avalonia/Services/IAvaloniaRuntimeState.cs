namespace IIoT.Edge.UI.Avalonia.Services;

public interface IAvaloniaRuntimeState
{
    event EventHandler? StateChanged;

    bool IsRuntimeStarted { get; }

    void SetRuntimeStarted(bool isRuntimeStarted);
}
