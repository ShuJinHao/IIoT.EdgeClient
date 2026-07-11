using Avalonia.Controls;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.UI.Shared.Avalonia.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Panels.Features.Equipment;

public partial class ProductionPlanSelectionWindow : Window
{
    private const int WindowCornerRadius = 16;

    public event Action<ProductionPlanOption?>? Completed;

    public ProductionPlanSelectionWindow()
    {
        InitializeComponent();
        EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius);
    }

    [ActivatorUtilitiesConstructor]
    public ProductionPlanSelectionWindow(ProductionPlanSelectionWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void OnConfirmClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ProductionPlanSelectionWindowViewModel { SelectedPlan: ProductionPlanOption plan })
        {
            Completed?.Invoke(plan);
            Close();
        }
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Completed?.Invoke(null);
        Close();
    }

}
