using Avalonia.Controls;

namespace IIoT.Edge.UI.Avalonia.Modularity;

public sealed class AvaloniaViewRegistration
{
    public required string ViewId { get; init; }

    public required Type ViewType { get; init; }

    public required Type ViewModelType { get; init; }

    public Func<IServiceProvider, object>? ViewModelFactory { get; init; }

    public bool CacheView { get; init; } = true;

    public Control CreateView(IServiceProvider services)
    {
        if (Activator.CreateInstance(ViewType) is not Control view)
        {
            throw new InvalidOperationException($"View type {ViewType.FullName} must inherit Avalonia.Controls.Control.");
        }

        view.DataContext = ViewModelFactory?.Invoke(services)
            ?? services.GetService(ViewModelType)
            ?? Activator.CreateInstance(ViewModelType)
            ?? throw new InvalidOperationException($"Cannot create view model {ViewModelType.FullName}.");

        return view;
    }
}
