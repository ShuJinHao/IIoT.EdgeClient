using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.DataView;

/// <summary>
/// 生产数据查询页面视图。
/// </summary>
public partial class DataViewPage : UserControl
{
    private bool _activated;

    public DataViewPage()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public DataViewPage(DataViewModel viewModel)
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
