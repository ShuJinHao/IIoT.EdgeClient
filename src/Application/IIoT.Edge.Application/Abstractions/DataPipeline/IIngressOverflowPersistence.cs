using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline;

public interface IIngressOverflowPersistence
{
    ValueTask<DataPipelineEnqueueResult> PersistOverflowAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default);
}
