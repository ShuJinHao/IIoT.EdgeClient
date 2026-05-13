using Avalonia.Threading;

namespace IIoT.Edge.UI.Avalonia.Services;

public sealed class AvaloniaTimerFactory : IAvaloniaTimerFactory
{
    public IAvaloniaTimer Create(TimeSpan interval)
        => new AvaloniaTimer(interval);

    private sealed class AvaloniaTimer : IAvaloniaTimer
    {
        private readonly DispatcherTimer _timer;

        public AvaloniaTimer(TimeSpan interval)
        {
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += (_, args) => Tick?.Invoke(this, args);
        }

        public event EventHandler? Tick;

        public TimeSpan Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        public bool IsEnabled => _timer.IsEnabled;

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();
    }
}
