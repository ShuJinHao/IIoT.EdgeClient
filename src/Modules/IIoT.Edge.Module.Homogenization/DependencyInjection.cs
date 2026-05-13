using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Homogenization.Presentation;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Homogenization;

/// <summary>
/// 匀浆 WPF 插件入口，只负责接入 WPF 页面。
/// </summary>
public sealed class DependencyInjection : HomogenizationModuleBase
{
    public new const string ModuleKey = HomogenizationModuleBase.ModuleKey;

    protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
    {
        base.ConfigureModuleServices(builder);
        builder.Services.AddSingleton<HomogenizationDataViewModel>();
    }

    protected override void RegisterModuleViews(IEdgeProcessModuleBuilder builder)
        => builder.RegisterHomogenizationViews();
}
