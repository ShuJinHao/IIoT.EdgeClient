namespace IIoT.Edge.Application.Abstractions.DataPipeline;

/// <summary>
/// Failure handling semantics for pipeline consumers.
/// </summary>
public enum ConsumerFailureMode
{
    BestEffort = 0,
    Durable = 1
}
