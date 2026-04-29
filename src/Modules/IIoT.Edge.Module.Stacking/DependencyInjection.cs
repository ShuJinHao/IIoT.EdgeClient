using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Integration;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.Module.Stacking.Presentation;
using IIoT.Edge.Module.Stacking.Runtime;
using IIoT.Edge.Module.Stacking.Samples;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Stacking;

public sealed class DependencyInjection : EdgeProcessModuleBase<StackingCellData>
{
    public const string ModuleKey = StackingModuleConstants.ModuleId;

    public override string ModuleId => ModuleKey;

    public override string ProcessType => StackingModuleConstants.ProcessType;

    public override string DisplayName => "叠片";

    protected override ProcessUploadMode CloudUploadMode => ProcessUploadMode.Single;

    protected override IStationRuntimeFactory CreateRuntimeFactory()
        => new StackingStationRuntimeFactory();

    protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
    {
        builder.Services.AddSingleton<IProcessCloudUploader, StackingCloudUploader>();
        builder.Services.AddSingleton<IModuleHardwareProfileProvider, StackingHardwareProfileProvider>();
        builder.Services.AddSingleton<IDevelopmentSampleContributor, StackingDevelopmentSampleContributor>();
    }

    protected override void RegisterModuleViews(IEdgeProcessModuleBuilder builder)
        => builder.RegisterStackingViews();
}

