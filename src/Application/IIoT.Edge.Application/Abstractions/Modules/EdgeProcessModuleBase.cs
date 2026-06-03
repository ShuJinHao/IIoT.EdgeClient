using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Application.Abstractions.Modules;

public abstract class EdgeProcessModuleBase<TCellData> : IEdgeProcessModule
    where TCellData : CellDataBase
{
    public abstract string ModuleId { get; }

    public virtual string ProcessType => ModuleId;

    public abstract string DisplayName { get; }

    protected abstract ProcessUploadMode CloudUploadMode { get; }

    protected virtual ProcessUploadMode? MesUploadMode => null;

    protected abstract IStationRuntimeFactory CreateRuntimeFactory();

    public void Configure(IEdgeProcessModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ConfigureModuleServices(builder);

        builder.RegisterCellData(typeof(TCellData));
        builder.RegisterRuntimeFactory(CreateRuntimeFactory());
        builder.RegisterCloudUploader(CloudUploadMode);

        if (MesUploadMode is { } mesUploadMode)
        {
            builder.RegisterMesUploader(mesUploadMode);
        }

        RegisterModuleViews(builder);
    }

    protected virtual void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
    {
    }

    protected abstract void RegisterModuleViews(IEdgeProcessModuleBuilder builder);
}
