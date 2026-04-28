using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Injection.Integration;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.Module.Injection.Presentation;
using IIoT.Edge.Module.Injection.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Injection;

public sealed class DependencyInjection : IEdgeProcessModule
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
        builder.RegisterCloudUploader(ProcessUploadMode.Batch);
        builder.RegisterInjectionViews();
    }
}

