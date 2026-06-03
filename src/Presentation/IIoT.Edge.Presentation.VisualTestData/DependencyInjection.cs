using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Application.Features.Production.DataView;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Monitor;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IIoT.Edge.Presentation.VisualTestData;

/// <summary>
/// UI 视觉验收测试数据注册入口。只允许在显式开关开启时替换展示层 facade。
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddVisualTestDataPresentation(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(VisualTestDataOptions.SectionName)
            .Get<VisualTestDataOptions>() ?? new VisualTestDataOptions();

        services.AddSingleton(options);
        if (!options.Enabled)
        {
            return services;
        }

        services.Replace(ServiceDescriptor.Transient<IEquipmentPanelService, VisualTestEquipmentPanelService>());
        services.Replace(ServiceDescriptor.Transient<ICapacityQueryFacade, VisualTestCapacityQueryFacade>());
        services.Replace(ServiceDescriptor.Transient<IProductionDataQueryFacade, VisualTestProductionDataQueryFacade>());
        services.Replace(ServiceDescriptor.Transient<IMonitorSnapshotQueryFacade, VisualTestMonitorSnapshotQueryFacade>());
        return services;
    }
}
