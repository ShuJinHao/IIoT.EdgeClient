using Avalonia.Controls;

namespace IIoT.Edge.UI.Avalonia.Modularity;

public interface IAvaloniaNavigationService
{
    object? CurrentViewModel { get; }

    Control? CurrentView { get; }

    event Action<object?>? Navigated;

    void NavigateTo(string viewId);
}
