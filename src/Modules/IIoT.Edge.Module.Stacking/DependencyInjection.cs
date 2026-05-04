using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Stacking.Config.Hardware;
using IIoT.Edge.Module.Stacking.Config.Parameters;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Integration;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.Module.Stacking.Presentation;
using IIoT.Edge.Module.Stacking.Runtime;
using IIoT.Edge.Module.Stacking.Samples;
using Microsoft.Extensions.DependencyInjection;
using StackingCloudUploadChannel = IIoT.Edge.Application.Modules.Cloud.ICloudUploadChannel<
    IIoT.Edge.Module.Stacking.Payload.StackingCellData,
    object>;

namespace IIoT.Edge.Module.Stacking;

/// <summary>
/// 叠片插件入口，声明叠片电芯类型、运行时、Cloud 上传、硬件模板、开发样本和标准导航页面。
/// </summary>
public sealed class DependencyInjection : EdgeProcessModuleBase<StackingCellData>
{
    /// <summary>
    /// 叠片模块唯一标识。
    /// </summary>
    public const string ModuleKey = StackingModuleConstants.ModuleId;

    public override string ModuleId => ModuleKey;

    public override string ProcessType => StackingModuleConstants.ProcessType;

    public override string DisplayName => "叠片";

    protected override ProcessUploadMode CloudUploadMode => ProcessUploadMode.Single;

    protected override IStationRuntimeFactory CreateRuntimeFactory()
        => new StackingStationRuntimeFactory();

    protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
    {
        builder.RegisterParameters<MesParam, CloudParam, BusinessParam>();

        builder.Services.AddSingleton<StackingCloudUploader>();
        builder.Services.AddSingleton<StackingCloudUploadChannel>(sp =>
            sp.GetRequiredService<StackingCloudUploader>());
        builder.Services.AddSingleton<IProcessCloudUploader>(sp =>
            sp.GetRequiredService<StackingCloudUploader>());
        builder.RegisterPlcSignalProfile<StackingSignal, StackingPlcSignalProfile>();
        builder.RegisterHardwareProfile<StackingHardwareProfileProvider>();
        builder.RegisterDevelopmentSample<StackingDevelopmentSampleContributor>();
    }

    protected override void RegisterModuleViews(IEdgeProcessModuleBuilder builder)
        => builder.RegisterStackingViews();
}
