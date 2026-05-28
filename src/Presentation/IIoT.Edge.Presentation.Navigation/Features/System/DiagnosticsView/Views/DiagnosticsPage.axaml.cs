using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

/// <summary>
/// 系统诊断页面视图。
/// </summary>
public partial class DiagnosticsPage : UserControl
{
    private bool _activated;

    public DiagnosticsPage()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public DiagnosticsPage(DiagnosticsViewModel viewModel)
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
