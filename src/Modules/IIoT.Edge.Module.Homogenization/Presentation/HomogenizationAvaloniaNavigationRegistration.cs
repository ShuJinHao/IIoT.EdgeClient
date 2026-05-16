using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Descriptors;
using IIoT.Edge.Module.Homogenization.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Homogenization.Presentation;

public static class HomogenizationAvaloniaNavigationRegistration
{
    public static IEdgeProcessModuleBuilder RegisterHomogenizationAvaloniaViews(this IEdgeProcessModuleBuilder builder)
    {
        var viewId = $"{DependencyInjection.ModuleKey}.DataView";
        builder.RegisterMenu(new ModuleMenuDescriptor
        {
            Title = "数据",
            TitleResourceKey = "Homogenization_Menu_Data",
            ViewId = viewId,
            Icon = "Table",
            Order = 1
        });
        builder.RegisterDocumentPanel(
            new ModulePanelDescriptor
            {
                Title = "匀浆出料数据",
                TitleResourceKey = "Homogenization_Title_Data",
                ContentId = viewId,
                InitialPosition = ModulePanelPosition.Main
            },
            typeof(HomogenizationDataPage),
            typeof(HomogenizationDataViewModel),
            serviceProvider => serviceProvider.GetRequiredService<HomogenizationDataViewModel>());
        return builder;
    }
}
