using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.DieCutting.Presentation.Views;

public partial class DieCuttingDataPage : UserControl
{
    private bool _activated;

    public DieCuttingDataPage()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public DieCuttingDataPage(DieCuttingDataViewModel viewModel)
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
