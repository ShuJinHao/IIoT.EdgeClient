namespace IIoT.Edge.SharedKernel.DataPipeline;

public interface IFallbackRecord
{
    long Id { get; }

    string ProcessType { get; }

    string CellDataJson { get; }

    string FailedTarget { get; }
}
