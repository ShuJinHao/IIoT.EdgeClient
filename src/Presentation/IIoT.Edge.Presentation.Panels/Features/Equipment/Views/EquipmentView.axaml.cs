using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Panels.Features.Equipment;

public partial class EquipmentView : UserControl
{
    private bool _activated;

    public EquipmentView()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public EquipmentView(EquipmentViewModel viewModel)
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
