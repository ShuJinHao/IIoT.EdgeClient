using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Presentation.Panels.Features.Equipment;
using IIoT.Edge.Presentation.Panels.Features.SysLog;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Panels;

public static class DependencyInjection
{
    public static IServiceCollection AddPanelPresentation(this IServiceCollection services)
    {
        services.AddSingleton<ILogService, Log4NetLogService>();

        services.AddSingleton<LogDisplayService>();
        services.AddSingleton<ISystemLogDisplayStore>(sp => sp.GetRequiredService<LogDisplayService>());

        services.AddSingleton<EquipmentViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddTransient<ProductionPlanSelectionWindowViewModel>();
        services.AddTransient<ProductionPlanSelectionWindow>();
        services.AddSingleton<IProductionPlanSelectionPopupService, ProductionPlanSelectionPopupService>();
        services.AddTransient(sp => new EquipmentView(sp.GetRequiredService<EquipmentViewModel>()));
        services.AddTransient(sp => new LogView(sp.GetRequiredService<LogViewModel>()));
        return services;
    }

    public static IViewRegistry RegisterPanelViews(this IViewRegistry registry)
    {
        registry.RegisterAnchorable(
            new AnchorableInfo
            {
                Title = "设备信息",
                TitleResourceKey = "Shell_EquipmentInfo",
                ContentId = "Core.Equipment",
                InitialPosition = AnchorablePosition.Right,
                IsVisible = true
            },
            typeof(EquipmentView),
            typeof(EquipmentViewModel));

        registry.RegisterAnchorable(
            new AnchorableInfo
            {
                Title = "系统日志",
                TitleResourceKey = "Shell_SystemLog",
                ContentId = "Core.SysLog",
                InitialPosition = AnchorablePosition.Right,
                IsVisible = true
            },
            typeof(LogView),
            typeof(LogViewModel));

        return registry;
    }
}
