namespace IIoT.Edge.UI.Avalonia.Services;

public interface IAvaloniaWindowService
{
    string MaxRestoreIcon { get; }

    event EventHandler? StateChanged;

    void Minimize();

    void ToggleMaximize();

    void Close();
}
