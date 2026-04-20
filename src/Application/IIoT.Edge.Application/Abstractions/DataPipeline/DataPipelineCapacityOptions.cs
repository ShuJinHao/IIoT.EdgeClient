namespace IIoT.Edge.Application.Abstractions.DataPipeline;

public sealed class DataPipelineCapacityOptions
{
    public const string SectionName = "DataPipelineCapacity";

    public DataPipelineChannelCapacityOptions Cloud { get; set; } = new();

    public DataPipelineChannelCapacityOptions Mes { get; set; } = new();
}

public sealed class DataPipelineChannelCapacityOptions
{
    public int RetryTotalLimit { get; set; } = 5000;

    public int RetryPerProcessTypeLimit { get; set; } = 2000;

    public int FallbackTotalLimit { get; set; } = 1000;
}
