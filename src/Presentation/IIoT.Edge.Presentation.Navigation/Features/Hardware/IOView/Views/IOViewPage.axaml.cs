using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

/// <summary>
/// IO 交互页面视图。
/// </summary>
public partial class IOViewPage : UserControl
{
    private bool _activated;

    public IOViewPage()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public IOViewPage(IoViewViewModel viewModel)
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
