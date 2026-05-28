using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

/// <summary>
/// 硬件配置主页面视图。
/// </summary>
public partial class HardwareConfigPage : UserControl
{
    private bool _activated;

    public HardwareConfigPage()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public HardwareConfigPage(HardwareConfigViewModel viewModel)
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
