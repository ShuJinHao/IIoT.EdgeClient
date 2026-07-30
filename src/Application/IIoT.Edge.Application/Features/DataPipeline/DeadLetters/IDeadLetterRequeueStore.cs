using IIoT.Edge.Module.Contracts.DataPipeline;

namespace IIoT.Edge.Application.Features.DataPipeline.DeadLetters;

public interface ICloudDeadLetterRequeueStore
{
    Task SaveRequeuedAsync(DeadLetterRecord record);
}

public interface IMesDeadLetterRequeueStore
{
    Task SaveRequeuedAsync(DeadLetterRecord record);
}
