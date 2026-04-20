using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Module.Abstractions;

namespace IIoT.Edge.Shell.Core;

public sealed class DevelopmentSampleInitializer : IDevelopmentSampleInitializer
{
    private readonly ILogService _logger;
    private readonly IReadOnlyList<IDevelopmentSampleContributor> _contributors;

    public DevelopmentSampleInitializer(
        ILogService logger,
        IEnumerable<IDevelopmentSampleContributor> contributors)
    {
        _logger = logger;
        _contributors = contributors
            .OrderBy(x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task EnsureConfigurationSamplesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var contributor in _contributors)
        {
            _logger.Info($"[DevSamples] Applying configuration samples for module '{contributor.ModuleId}'.");
            await contributor.EnsureConfigurationSamplesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task EnsureRuntimeSamplesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var contributor in _contributors)
        {
            _logger.Info($"[DevSamples] Applying runtime samples for module '{contributor.ModuleId}'.");
            await contributor.EnsureRuntimeSamplesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
