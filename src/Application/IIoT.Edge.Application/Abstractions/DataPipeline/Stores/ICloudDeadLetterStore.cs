using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

public interface ICloudDeadLetterStore : IDeadLetterDiagnosticsStore
{
    Task SaveAsync(DeadLetterRecord record);
}
