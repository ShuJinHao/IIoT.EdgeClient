using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.Homogenization.Integration;

public sealed class HomogenizationCloudUploader : ProcessCloudUploaderBase<HomogenizationCellData, object>
{
    private const string UploadPathValue = "/api/v1/edge/pass-stations/homogenization";

    public HomogenizationCloudUploader(ICloudHttpClient cloudHttp, ILogService logger)
        : base(DependencyInjection.ModuleKey, ProcessUploadMode.Single, UploadPathValue, cloudHttp, logger)
    {
    }

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
