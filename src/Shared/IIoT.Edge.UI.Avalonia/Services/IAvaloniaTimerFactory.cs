namespace IIoT.Edge.UI.Avalonia.Services;

public interface IAvaloniaTimerFactory
{
    IAvaloniaTimer Create(TimeSpan interval);
}
