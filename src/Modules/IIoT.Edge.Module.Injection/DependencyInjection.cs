using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Injection.Integration;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.Module.Injection.Presentation;
using IIoT.Edge.Module.Injection.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Injection;

public sealed class DependencyInjection : EdgeProcessModuleBase<InjectionCellData>
{
    public const string ModuleKey = "Injection";

    public override string ModuleId => ModuleKey;

    public override string ProcessType => ModuleKey;

    public override string DisplayName => "注液";

    protected override ProcessUploadMode CloudUploadMode => ProcessUploadMode.Batch;

    protected override IStationRuntimeFactory CreateRuntimeFactory()
        => new InjectionStationRuntimeFactory();

    protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
        => builder.Services.AddSingleton<IProcessCloudUploader, InjectionCloudUploader>();

    protected override void RegisterModuleViews(IEdgeProcessModuleBuilder builder)
        => builder.RegisterInjectionViews();
}

