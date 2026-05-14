namespace IIoT.Edge.UI.Avalonia.Services;

public interface IAvaloniaRuntimeState
{
    event EventHandler? StateChanged;

    bool IsRuntimeStarted { get; }

    AvaloniaRuntimeStateSnapshot Snapshot { get; }

    void SetStatus(
        AvaloniaRuntimeStatus status,
        string? detailText = null,
        string? diagnosticsSummary = null,
        string? diagnosticsLogPath = null);

    void SetRuntimeStarted(bool isRuntimeStarted);
}
