using IIoT.Edge.Module.Abstractions;

namespace IIoT.Edge.Module.Homogenization.Samples;

public sealed class HomogenizationDevelopmentSampleContributor : IDevelopmentSampleContributor
{
    public string ModuleId => "Homogenization";

    public Task EnsureConfigurationSamplesAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task EnsureRuntimeSamplesAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}