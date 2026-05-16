namespace IIoT.Edge.Host.Bootstrap;

public interface IDevelopmentSampleInitializer
{
    Task EnsureConfigurationSamplesAsync(CancellationToken cancellationToken = default);

    Task EnsureRuntimeSamplesAsync(CancellationToken cancellationToken = default);
}
