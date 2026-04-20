using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

public interface ICloudDeadLetterStore
{
    Task SaveAsync(DeadLetterRecord record);

    Task<int> GetCountAsync();
}
