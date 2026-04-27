using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.Homogenization.Integration;

public sealed class HomogenizationCloudUploader : ProcessCloudUploaderBase<HomogenizationCellData, object>
{
    public HomogenizationCloudUploader(ICloudHttpClient cloudHttp, ILogService logger)
        : base(cloudHttp, logger)
    {
    }

    public override string ProcessType => DependencyInjection.ModuleKey;

    public override ProcessUploadMode UploadMode => ProcessUploadMode.Single;

    protected override string UploadPath => "/api/v1/edge/pass-stations/homogenization";

    protected override Task<CloudCallResult?> CheckBeforeUploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken)
        => Task.FromResult<CloudCallResult?>(
            CloudCallResult.Failure(CloudCallOutcome.Exception, "homogenization_cloud_upload_not_implemented"));

    protected override object BuildPayload(
        ProcessCloudUploadContext context,
        IReadOnlyList<HomogenizationCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records)
        => throw new NotSupportedException("匀浆云端上传尚未接入。");
}
