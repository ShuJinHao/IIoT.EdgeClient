using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.Homogenization.Integration;

public sealed class HomogenizationMesUploader : ProcessMesUploaderBase<HomogenizationCellData>
{
    private readonly IHomogenizationMesApiService _mesApiService;

    public HomogenizationMesUploader(
        IHomogenizationMesApiService mesApiService,
        ILogService logger)
        : base(logger)
    {
        _mesApiService = mesApiService;
    }

    public override string ProcessType => HomogenizationModuleConstants.ProcessType;

    protected override Task<MesCallResult> UploadCellAsync(
        ProcessMesUploadContext context,
        HomogenizationCellData cellData,
        CellCompletedRecord record,
        CancellationToken cancellationToken)
        => _mesApiService.UploadOutboundAsync(context.Device, cellData, cancellationToken);
}
