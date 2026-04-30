using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

public interface IDeadLetterDiagnosticsStore
{
    Task<int> GetCountAsync();

    Task<IReadOnlyList<DeadLetterGroupSummary>> GetGroupSummaryAsync();

    Task<IReadOnlyList<DeadLetterRecord>> GetLatestAsync(int count = 20);
}
