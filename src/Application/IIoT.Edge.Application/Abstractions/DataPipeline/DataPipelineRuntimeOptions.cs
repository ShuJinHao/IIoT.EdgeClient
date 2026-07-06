namespace IIoT.Edge.Application.Abstractions.DataPipeline;

public sealed class DataPipelineRuntimeOptions
{
    public const string SectionName = "DataPipeline";
    public const int DefaultConsumerCallTimeoutSeconds = 30;
    public const int DefaultDurableOutletQueueCapacity = 5000;

    public int ConsumerCallTimeoutSeconds { get; set; } = DefaultConsumerCallTimeoutSeconds;
    public int DurableOutletQueueCapacity { get; set; } = DefaultDurableOutletQueueCapacity;

    public TimeSpan GetConsumerCallTimeout()
        => TimeSpan.FromSeconds(ConsumerCallTimeoutSeconds > 0
            ? ConsumerCallTimeoutSeconds
            : DefaultConsumerCallTimeoutSeconds);

    public int GetDurableOutletQueueCapacity()
        => DurableOutletQueueCapacity > 0
            ? DurableOutletQueueCapacity
            : DefaultDurableOutletQueueCapacity;
}
