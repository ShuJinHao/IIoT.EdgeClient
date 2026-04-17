using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.Modules;

public enum MesUploadMode
{
    Single = 0
}

public sealed record ProcessMesUploadContext(DeviceSession Device);

public interface IProcessMesUploader
{
    string ProcessType { get; }

    MesUploadMode UploadMode { get; }

    Task<bool> UploadAsync(
        ProcessMesUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default);
}
