using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Application.Modules.Samples;

public abstract class DevelopmentSampleContributorBase : IDevelopmentSampleContributor
{
    private readonly IReadOnlyDictionary<string, IModuleHardwareProfileProvider> _hardwareProfiles;

    protected DevelopmentSampleContributorBase(
        IConfiguration configuration,
        IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles)
    {
        Configuration = configuration;
        _hardwareProfiles = BuildUniqueHardwareProfiles(hardwareProfiles);
    }

    public abstract string ModuleId { get; }

    protected IConfiguration Configuration { get; }

    public async Task EnsureConfigurationSamplesAsync(CancellationToken cancellationToken = default)
    {
        if (!ShouldEnsureConfigurationSamples())
        {
            OnConfigurationSamplesSkipped();
            return;
        }

        await EnsureConfigurationSamplesCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureRuntimeSamplesAsync(CancellationToken cancellationToken = default)
    {
        if (!ShouldEnsureRuntimeSamples())
        {
            OnRuntimeSamplesSkipped();
            return;
        }

        await EnsureRuntimeSamplesCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    protected TOptions BindOptions<TOptions>(string sectionName)
        where TOptions : class, new()
    {
        var options = new TOptions();
        Configuration.GetSection(sectionName).Bind(options);
        return options;
    }

    protected IModuleHardwareProfileProvider GetHardwareProfile(string? missingMessage = null)
    {
        if (_hardwareProfiles.TryGetValue(ModuleId, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException(
            missingMessage ?? $"模块“{ModuleId}”缺少硬件模板提供器。");
    }

    protected virtual bool ShouldEnsureConfigurationSamples()
        => true;

    protected virtual bool ShouldEnsureRuntimeSamples()
        => false;

    protected virtual void OnConfigurationSamplesSkipped()
    {
    }

    protected virtual void OnRuntimeSamplesSkipped()
    {
    }

    protected abstract Task EnsureConfigurationSamplesCoreAsync(CancellationToken cancellationToken);

    protected virtual Task EnsureRuntimeSamplesCoreAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    private static IReadOnlyDictionary<string, IModuleHardwareProfileProvider> BuildUniqueHardwareProfiles(
        IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles)
    {
        var result = new Dictionary<string, IModuleHardwareProfileProvider>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in hardwareProfiles)
        {
            string moduleId;
            try
            {
                moduleId = provider.ModuleId;
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(moduleId) || duplicates.Contains(moduleId))
                continue;

            if (!result.TryAdd(moduleId, provider))
            {
                result.Remove(moduleId);
                duplicates.Add(moduleId);
            }
        }

        return result;
    }
}
