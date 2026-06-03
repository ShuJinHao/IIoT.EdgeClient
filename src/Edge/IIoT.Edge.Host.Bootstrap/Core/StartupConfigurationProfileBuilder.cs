using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Modules.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Shell.Core;

internal sealed class StartupConfigurationProfileBuilder(
    IConfiguration configuration,
    EdgeRuntimePaths runtimePaths)
    : IStartupConfigurationProfileBuilder
{
    public ConfigurationProfileSnapshot Build()
    {
        var environmentName = configuration["Shell:Environment"]?.Trim();
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            environmentName = "Production";
        }

        var machineProfile = configuration["Shell:MachineProfile"]?.Trim();
        var machineProfileFileName = configuration["Shell:MachineProfileFileName"]?.Trim();
        var machineProfileLoaded = bool.TryParse(configuration["Shell:MachineProfileLoaded"], out var loaded)
            && loaded;

        return new ConfigurationProfileSnapshot(
            environmentName,
            string.IsNullOrWhiteSpace(machineProfile) ? null : machineProfile,
            string.IsNullOrWhiteSpace(machineProfileFileName) ? null : machineProfileFileName,
            machineProfileLoaded,
            runtimePaths.RuntimeDataRoot);
    }
}
