using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;

/// <summary>
/// 配方页面视图。
/// </summary>
public partial class RecipeViewPage : UserControl
{
    private bool _activated;

    public RecipeViewPage()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public RecipeViewPage(RecipeViewModel viewModel)
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
