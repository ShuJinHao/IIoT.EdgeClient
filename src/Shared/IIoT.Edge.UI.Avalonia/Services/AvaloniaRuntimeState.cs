namespace IIoT.Edge.UI.Avalonia.Services;

public sealed class AvaloniaRuntimeState : IAvaloniaRuntimeState
{
    private bool _isRuntimeStarted;

    public event EventHandler? StateChanged;

    public bool IsRuntimeStarted
    {
        get => _isRuntimeStarted;
        private set
        {
            if (_isRuntimeStarted == value)
            {
                return;
            }

            _isRuntimeStarted = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetRuntimeStarted(bool isRuntimeStarted)
    {
        IsRuntimeStarted = isRuntimeStarted;
    }
}
