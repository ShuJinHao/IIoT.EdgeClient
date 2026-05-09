using IIoT.Edge.Module.Homogenization.Presentation.Views;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.PluginSystem;

namespace IIoT.Edge.Module.Homogenization.Presentation;

public static class HomogenizationNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterHomogenizationViews(this IEdgeProcessModuleBuilder builder)
        => builder.RegisterStandardModuleViews(
            HomogenizationModuleIdentity.ModuleId,
            "数据",
            "Homogenization_Menu_Data",
            customDataViewType: typeof(HomogenizationDataPage),
            customDataViewModelType: typeof(HomogenizationDataViewModel));
}
