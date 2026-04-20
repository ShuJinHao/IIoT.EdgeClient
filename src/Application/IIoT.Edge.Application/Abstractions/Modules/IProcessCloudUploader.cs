using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.Modules;

public enum ProcessUploadMode
{
    Single = 0,
    Batch = 1
}

public sealed record ProcessCloudUploadContext(DeviceSession Device);

public interface IProcessCloudUploader
{
    string ProcessType { get; }

    ProcessUploadMode UploadMode { get; }

    Task<CloudCallResult> UploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default);
}
