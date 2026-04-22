using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.Homogenization.Integration;

public sealed class HomogenizationCloudUploader : IProcessCloudUploader
{
    public string ProcessType => "Homogenization";

    public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

    public Task<CloudCallResult> UploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(records);

        return Task.FromResult(
            CloudCallResult.Failure(
                CloudCallOutcome.Exception,
                "Homogenization_uploader_stub_not_implemented"));
    }
}