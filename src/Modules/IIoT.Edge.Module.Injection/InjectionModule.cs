using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Module.Injection.Integration;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.Module.Injection.Presentation;
using IIoT.Edge.Module.Injection.Runtime;
using IIoT.Edge.Plugin.Shared.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Injection;

public sealed class InjectionModule : IEdgeProcessModule
{
    public const string ModuleKey = "Injection";

    public string ModuleId => ModuleKey;

    public string ProcessType => ModuleKey;

    public string DisplayName => "注液";

    public void Configure(IEdgeProcessModuleBuilder builder)
    {
        builder.Services.AddSingleton<IProcessCloudUploader, InjectionCloudUploader>();

        builder.RegisterCellData(typeof(InjectionCellData));
        builder.RegisterRuntimeFactory(new InjectionStationRuntimeFactory());
        builder.RegisterCloudUploader(PluginCloudUploadMode.Batch);
        builder.RegisterInjectionViews();
    }
}
