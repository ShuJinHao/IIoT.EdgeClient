using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

public interface IMesDeadLetterStore : IDeadLetterDiagnosticsStore
{
    Task SaveAsync(DeadLetterRecord record);
}
