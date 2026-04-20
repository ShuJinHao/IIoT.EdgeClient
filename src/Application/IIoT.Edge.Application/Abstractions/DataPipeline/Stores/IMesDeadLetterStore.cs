using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

public interface IMesDeadLetterStore
{
    Task SaveAsync(DeadLetterRecord record);

    Task<int> GetCountAsync();
}
