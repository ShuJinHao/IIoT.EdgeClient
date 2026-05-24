using Avalonia.Controls;
using IIoT.Edge.Presentation.Navigation.Features.Dashboard;
using IIoT.Edge.UI.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

public partial class DashboardPreviewView : UserControl
{
    private const string DesignPreviewEnvironmentVariable = "IIOT_EDGE_DASHBOARD_PREVIEW_DATA";

    private bool _activated;
    private DashboardPreviewRuntimeViewModel? _runtimeViewModel;
    private IDisposable? _previewDataContext;

    public DashboardPreviewView()
    {
        InitializeComponent();
    }

    [ActivatorUtilitiesConstructor]
    public DashboardPreviewView(DashboardViewModel viewModel, IAppLanguageService languageService)
        : this()
    {
        if (UseDesignPreviewData())
        {
            var designViewModel = new DashboardPreviewDesignViewModel(languageService);
            _previewDataContext = designViewModel;
            DataContext = designViewModel;
            return;
        }

        _runtimeViewModel = new DashboardPreviewRuntimeViewModel(viewModel, languageService);
        _previewDataContext = _runtimeViewModel;
        DataContext = _runtimeViewModel;
        AttachedToVisualTree += async (_, _) =>
        {
            if (_activated)
            {
                return;
            }

            _activated = true;
            await _runtimeViewModel.OnActivatedAsync();
        };
        DetachedFromVisualTree += async (_, _) =>
        {
            _activated = false;
            await _runtimeViewModel.OnDeactivatedAsync();
        };
    }

    private static bool UseDesignPreviewData()
        => string.Equals(
            Environment.GetEnvironmentVariable(DesignPreviewEnvironmentVariable),
            "design",
            StringComparison.OrdinalIgnoreCase);
}
