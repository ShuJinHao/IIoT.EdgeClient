using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.Modules;

public enum ProcessUploadMode
{
    Single = 0,
    Batch = 1
}

public sealed record ProcessUploadContext(DeviceSession Device);

public interface IProcessUploader<TResult>
{
    string ProcessType { get; }

    ProcessUploadMode UploadMode { get; }

    Task<TResult> UploadAsync(
        ProcessUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default);
}

public interface IProcessCloudUploader : IProcessUploader<CloudCallResult>
{
}
