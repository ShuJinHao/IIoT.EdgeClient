using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Injection.Config.Parameters;
using IIoT.Edge.Module.Injection.Integration;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.Module.Injection.Presentation;
using IIoT.Edge.Module.Injection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using InjectionCloudUploadChannel = IIoT.Edge.Application.Modules.Cloud.ICloudUploadChannel<
    IIoT.Edge.Module.Injection.Payload.InjectionCellData,
    object>;

namespace IIoT.Edge.Module.Injection;

/// <summary>
/// 注液插件入口，声明注液电芯类型、运行时工厂、Cloud 上传器和标准导航页面。
/// </summary>
public sealed class DependencyInjection : EdgeProcessModuleBase<InjectionCellData>
{
    /// <summary>
    /// 注液模块唯一标识，用于模块发现、工序类型和 DataPipeline 反序列化。
    /// </summary>
    public const string ModuleKey = "Injection";

    public override string ModuleId => ModuleKey;

    public override string ProcessType => ModuleKey;

    public override string DisplayName => "注液";

    protected override ProcessUploadMode CloudUploadMode => ProcessUploadMode.Batch;

    protected override IStationRuntimeFactory CreateRuntimeFactory()
        => new InjectionStationRuntimeFactory();

    protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
    {
        builder.RegisterParameters<MesParam, CloudParam, BusinessParam>();

        builder.Services.AddSingleton<InjectionCloudUploader>();
        builder.Services.AddSingleton<InjectionCloudUploadChannel>(sp =>
            sp.GetRequiredService<InjectionCloudUploader>());
        builder.Services.AddSingleton<IProcessCloudUploader>(sp =>
            sp.GetRequiredService<InjectionCloudUploader>());
    }

    protected override void RegisterModuleViews(IEdgeProcessModuleBuilder builder)
        => builder.RegisterInjectionViews();
}
