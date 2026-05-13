namespace IIoT.Edge.UI.Avalonia.Services;

public interface IAvaloniaTimer
{
    event EventHandler? Tick;

    TimeSpan Interval { get; set; }

    bool IsEnabled { get; }

    void Start();

    void Stop();
}
