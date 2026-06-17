using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Application.Abstractions.Modules;

public abstract class EdgeProcessModuleBase<TCellData> : IEdgeProcessModule
    where TCellData : CellDataBase
{
    public abstract string ModuleId { get; }

    public virtual string ProcessType => ModuleId;

    public abstract string DisplayName { get; }

    protected virtual ProcessUploadMode? CloudUploadMode => null;

    protected virtual ProcessUploadMode? MesUploadMode => null;

    protected abstract IStationRuntimeFactory CreateRuntimeFactory();

    public void Configure(IEdgeProcessModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ConfigureModuleServices(builder);

        builder.RegisterCellData(typeof(TCellData));
        builder.RegisterRuntimeFactory(CreateRuntimeFactory());
        if (CloudUploadMode is { } cloudUploadMode)
        {
            builder.RegisterCloudUploader(cloudUploadMode);
        }

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
