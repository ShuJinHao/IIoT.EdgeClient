using Microsoft.Extensions.Configuration;
using System.IO;

namespace IIoT.Edge.Shell.Core;

public sealed record ShellConfigurationLoadResult(
    IConfigurationRoot Configuration,
    string EnvironmentName,
    string? MachineProfile,
    string? MachineProfileFileName,
    bool IsMachineProfileLoaded);

public static class ShellConfigurationLoader
{
    public static ShellConfigurationLoadResult Load(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var environmentName = GetEnvironmentName();
        var bootstrapConfiguration = new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var machineProfile = bootstrapConfiguration["Shell:MachineProfile"]?.Trim();
        var machineProfileFileName = string.IsNullOrWhiteSpace(machineProfile)
            ? null
            : $"appsettings.machine.{machineProfile}.json";
        var machineProfilePath = machineProfileFileName is null
            ? null
            : Path.Combine(baseDirectory, machineProfileFileName);
        var machineProfileLoaded = machineProfilePath is not null
            && File.Exists(machineProfilePath);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);

        if (machineProfileFileName is not null)
        {
            configuration.AddJsonFile(machineProfileFileName, optional: true, reloadOnChange: true);
        }

        configuration
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shell:Environment"] = environmentName,
                ["Shell:MachineProfile"] = machineProfile,
                ["Shell:MachineProfileFileName"] = machineProfileFileName,
                ["Shell:MachineProfileLoaded"] = machineProfileLoaded.ToString()
            });

        return new ShellConfigurationLoadResult(
            configuration.Build(),
            environmentName,
            machineProfile,
            machineProfileFileName,
            machineProfileLoaded);
    }

    private static string GetEnvironmentName()
        => Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";
}
