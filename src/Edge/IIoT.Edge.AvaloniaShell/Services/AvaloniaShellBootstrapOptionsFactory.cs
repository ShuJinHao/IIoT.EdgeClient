using IIoT.Edge.Host.Bootstrap;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.AvaloniaShell.Services;

public sealed class AvaloniaShellBootstrapOptionsFactory : IAvaloniaShellBootstrapOptionsFactory
{
    private static readonly string[] DefaultModuleIds = ["Homogenization"];

    private readonly IShellConfigurationLoader _configurationLoader;
    private readonly IShellRuntimePathResolver _runtimePathResolver;

    public AvaloniaShellBootstrapOptionsFactory(
        IShellConfigurationLoader configurationLoader,
        IShellRuntimePathResolver runtimePathResolver)
    {
        _configurationLoader = configurationLoader ?? throw new ArgumentNullException(nameof(configurationLoader));
        _runtimePathResolver = runtimePathResolver ?? throw new ArgumentNullException(nameof(runtimePathResolver));
    }

    public AvaloniaHostBootstrapOptions Create(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var configurationResult = _configurationLoader.Load(baseDirectory);
        var runtimePaths = _runtimePathResolver.Resolve(baseDirectory, configurationResult.Configuration);
        var moduleIds = ResolveModuleIds(configurationResult.Configuration);

        return new AvaloniaHostBootstrapOptions(
            configurationResult.Configuration,
            runtimePaths,
            configurationResult.EnvironmentName,
            moduleIds,
            PluginDirectories: [baseDirectory]);
    }

    private static IReadOnlyCollection<string> ResolveModuleIds(IConfiguration configuration)
    {
        var configuredModuleIds = configuration
            .GetSection("Modules:Enabled")
            .Get<string[]>()
            ?.Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return configuredModuleIds is { Length: > 0 }
            ? configuredModuleIds
            : DefaultModuleIds;
    }
}
