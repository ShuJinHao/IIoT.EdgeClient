using Avalonia.Controls;
using IIoT.Edge.Module.Homogenization.Presentation;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Homogenization.Presentation.Views;

public partial class HomogenizationDataPage : UserControl
{
    private bool _activated;

    public HomogenizationDataPage()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public HomogenizationDataPage(HomogenizationDataViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        AttachedToVisualTree += async (_, _) =>
        {
            if (_activated)
            {
                return;
            }

            _activated = true;
            await viewModel.OnActivatedAsync();
        };
        DetachedFromVisualTree += async (_, _) =>
        {
            _activated = false;
            await viewModel.OnDeactivatedAsync();
        };
    }
}
