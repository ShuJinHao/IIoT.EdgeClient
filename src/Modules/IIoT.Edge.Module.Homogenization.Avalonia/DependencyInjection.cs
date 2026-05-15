using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Homogenization.Avalonia.Localization;
using IIoT.Edge.Module.Homogenization.Avalonia.Presentation;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Homogenization.Avalonia;

/// <summary>
/// 匀浆 Avalonia 插件入口，只负责接入 Avalonia 页面。
/// </summary>
public sealed class DependencyInjection : HomogenizationModuleBase
{
    public new const string ModuleKey = HomogenizationModuleBase.ModuleKey;

    protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
    {
        base.ConfigureModuleServices(builder);
        builder.Services.AddSingleton<HomogenizationDataViewModel>();
        builder.Services.AddSingleton<IIoT.Edge.UI.Avalonia.Localization.IAvaloniaResourceContributor, HomogenizationAvaloniaZhCnResources>();
        builder.Services.AddSingleton<IIoT.Edge.UI.Avalonia.Localization.IAvaloniaResourceContributor, HomogenizationAvaloniaEnUsResources>();
    }

    protected override void RegisterModuleViews(IEdgeProcessModuleBuilder builder)
        => builder.RegisterHomogenizationAvaloniaViews();
}
