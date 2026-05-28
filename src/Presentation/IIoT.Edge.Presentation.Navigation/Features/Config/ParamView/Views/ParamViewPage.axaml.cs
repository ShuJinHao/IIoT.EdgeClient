using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;

/// <summary>
/// 参数配置页面视图。
/// </summary>
public partial class ParamViewPage : UserControl
{
    private bool _activated;

    public ParamViewPage()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public ParamViewPage(ParamViewModel viewModel)
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
