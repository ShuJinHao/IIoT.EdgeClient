using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.PlcTaskBindingView;

/// <summary>
/// PLC 任务绑定页面视图。
/// </summary>
public partial class PlcTaskBindingPage : UserControl
{
    private bool _activated;

    public PlcTaskBindingPage()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public PlcTaskBindingPage(PlcTaskBindingViewModel viewModel)
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
