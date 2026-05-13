namespace IIoT.Edge.Application.Abstractions.Modules;

public interface IDevelopmentSampleContributor
{
    string ModuleId { get; }

    Task EnsureConfigurationSamplesAsync(CancellationToken cancellationToken = default);

    Task EnsureRuntimeSamplesAsync(CancellationToken cancellationToken = default);
}
