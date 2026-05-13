namespace IIoT.Edge.Shell.Core;

public interface IDevelopmentSampleInitializer
{
    Task EnsureConfigurationSamplesAsync(CancellationToken cancellationToken = default);

    Task EnsureRuntimeSamplesAsync(CancellationToken cancellationToken = default);
}
