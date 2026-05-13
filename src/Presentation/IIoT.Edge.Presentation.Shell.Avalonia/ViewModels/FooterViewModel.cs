using CommunityToolkit.Mvvm.ComponentModel;
using IIoT.Edge.UI.Avalonia.Mvvm;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Shell.Avalonia.ViewModels;

public sealed partial class FooterViewModel : AvaloniaViewModelBase
{
    private readonly DateTime _startTime = DateTime.Now;
    private readonly IAvaloniaTimer _timer;

    public FooterViewModel(IAvaloniaTimerFactory timerFactory)
    {
        _timer = timerFactory.Create(TimeSpan.FromSeconds(1));
        _timer.Tick += (_, _) => UpdateClock();
        UpdateClock();
        _timer.Start();
    }

    public override string ViewId => "Core.Footer";

    [ObservableProperty]
    private string currentTime = string.Empty;

    [ObservableProperty]
    private string upTime = "00:00:00";

    private void UpdateClock()
    {
        CurrentTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
        var elapsed = DateTime.Now - _startTime;
        UpTime = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }
}
