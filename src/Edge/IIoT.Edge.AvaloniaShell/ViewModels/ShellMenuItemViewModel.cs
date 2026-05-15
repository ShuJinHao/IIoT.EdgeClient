using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IIoT.Edge.AvaloniaShell.ViewModels;

public sealed partial class ShellMenuItemViewModel : ObservableObject
{
    private readonly Action<string> _navigate;

    public ShellMenuItemViewModel(string viewId, string title, Action<string> navigate)
    {
        ViewId = viewId;
        Title = title;
        _navigate = navigate;
    }

    public string ViewId { get; }

    [ObservableProperty]
    private string title;

    [RelayCommand]
    private void Navigate()
    {
        _navigate(ViewId);
    }
}
