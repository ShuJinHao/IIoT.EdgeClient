namespace IIoT.Edge.Application.Abstractions.DataPipeline;

/// <summary>
/// Signals a permanent record-level contract failure that must bypass retry and fallback.
/// The DataPipeline host persists the record directly to the target channel dead letter store.
/// </summary>
public sealed class DataPipelineNonRetryableException(string reasonCode) : Exception(reasonCode)
{
    public string ReasonCode { get; } = reasonCode;
}
