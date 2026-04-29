namespace IIoT.Edge.Application.Abstractions.DataPipeline;

public sealed class DataPipelineRuntimeOptions
{
    public const string SectionName = "DataPipeline";
    public const int DefaultConsumerCallTimeoutSeconds = 30;

    public int ConsumerCallTimeoutSeconds { get; set; } = DefaultConsumerCallTimeoutSeconds;

    public TimeSpan GetConsumerCallTimeout()
        => TimeSpan.FromSeconds(ConsumerCallTimeoutSeconds > 0
            ? ConsumerCallTimeoutSeconds
            : DefaultConsumerCallTimeoutSeconds);
}
