using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

/// <summary>
/// 实时监控页面视图。
/// </summary>
public partial class MonitorViewPage : UserControl
{
    private bool _activated;

    public MonitorViewPage()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public MonitorViewPage(MonitorViewModel viewModel)
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
