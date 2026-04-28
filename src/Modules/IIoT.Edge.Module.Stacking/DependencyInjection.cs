using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Integration;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.Module.Stacking.Presentation;
using IIoT.Edge.Module.Stacking.Runtime;
using IIoT.Edge.Module.Stacking.Samples;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Stacking;

public sealed class DependencyInjection : IEdgeProcessModule
{
    public const string ModuleKey = StackingModuleConstants.ModuleId;

    public string ModuleId => ModuleKey;

    public string ProcessType => StackingModuleConstants.ProcessType;

    public string DisplayName => "叠片";

    public void Configure(IEdgeProcessModuleBuilder builder)
    {
        builder.Services.AddSingleton<IProcessCloudUploader, StackingCloudUploader>();
        builder.Services.AddSingleton<IModuleHardwareProfileProvider, StackingHardwareProfileProvider>();
        builder.Services.AddSingleton<IDevelopmentSampleContributor, StackingDevelopmentSampleContributor>();

        builder.RegisterCellData(typeof(StackingCellData));
        builder.RegisterRuntimeFactory(new StackingStationRuntimeFactory());
        builder.RegisterCloudUploader(ProcessUploadMode.Single);
        builder.RegisterStackingViews();
    }
}

