using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Presentation.Navigation.PluginSystem;
using IIoT.Edge.Module.DieCutting.Presentation.Views;

namespace IIoT.Edge.Module.DieCutting.Presentation;

public static class DieCuttingNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterDieCuttingViews(
        this IEdgeProcessModuleBuilder builder,
        string displayName)
        => builder.RegisterStandardModuleViews(
            builder.ModuleId,
            $"{displayName}采样",
            "DieCutting_Menu_Data",
            customDataViewType: typeof(DieCuttingDataPage),
            customDataViewModelType: typeof(DieCuttingDataViewModel),
            dataMenuTitle: null,
            dataMenuTitleResourceKey: null,
            cacheDataView: true,
            supportsRecipe: false);
}
