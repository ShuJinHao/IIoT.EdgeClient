using IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Features.Production.DataView;
using IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation;

public static class DependencyInjection
{
    public static IServiceCollection AddNavigationPresentation(this IServiceCollection services)
    {
        services.AddSingleton<ParamViewModel>();
        services.AddSingleton<IoViewViewModel>();
        services.AddSingleton<HardwareConfigViewModel>();
        services.AddSingleton<RecipeViewModel>();
        services.AddSingleton<CapacityViewModel>();
        services.AddSingleton<MonitorViewModel>();
        services.AddSingleton<DataViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();

        services.AddTransient<ParamViewPage>();
        services.AddTransient<IOViewPage>();
        services.AddTransient<HardwareConfigPage>();
        services.AddTransient<RecipeViewPage>();
        services.AddTransient<CapacityViewPage>();
        services.AddTransient<MonitorViewPage>();
        services.AddTransient<DataViewPage>();
        services.AddTransient<DiagnosticsPage>();

        return services;
    }
}
