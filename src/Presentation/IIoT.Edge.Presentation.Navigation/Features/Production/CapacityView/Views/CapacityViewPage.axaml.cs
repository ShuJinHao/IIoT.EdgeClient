using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;

/// <summary>
/// 产能查询页面视图。
/// </summary>
public partial class CapacityViewPage : UserControl
{
    private bool _activated;

    public CapacityViewPage()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public CapacityViewPage(CapacityViewModel viewModel)
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
