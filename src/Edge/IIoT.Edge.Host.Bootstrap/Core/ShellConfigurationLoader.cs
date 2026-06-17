using IIoT.Edge.SharedKernel.Configuration;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace IIoT.Edge.Shell.Core;

public sealed record ShellConfigurationLoadResult(
    IConfigurationRoot Configuration,
    string EnvironmentName,
    string? MachineProfile,
    string? MachineProfileFileName,
    bool IsMachineProfileLoaded)
{
    public string? MachineProfilePath { get; init; }

    public string? ExternalMachineProfilePath { get; init; }

    public bool IsExternalMachineProfileLoaded { get; init; }
}

public interface IShellConfigurationLoader
{
    ShellConfigurationLoadResult Load(string baseDirectory);
}

public sealed class ShellConfigurationLoader : IShellConfigurationLoader
{
    public ShellConfigurationLoadResult Load(string baseDirectory)
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
        var packagedMachineProfilePath = machineProfileFileName is null
            ? null
            : Path.Combine(baseDirectory, machineProfileFileName);
        var packagedMachineProfileLoaded = packagedMachineProfilePath is not null
            && File.Exists(packagedMachineProfilePath);
        var externalMachineProfilePath = string.IsNullOrWhiteSpace(machineProfile)
            ? null
            : EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(machineProfile, baseDirectory);

        if (externalMachineProfilePath is not null)
        {
            TryInitializeExternalMachineProfile(packagedMachineProfilePath, externalMachineProfilePath);
        }

        var externalMachineProfileLoaded = externalMachineProfilePath is not null
            && File.Exists(externalMachineProfilePath);
        var machineProfileLoaded = externalMachineProfileLoaded || packagedMachineProfileLoaded;
        var effectiveMachineProfilePath = externalMachineProfileLoaded
            ? externalMachineProfilePath
            : packagedMachineProfilePath;

        var configuration = new ConfigurationBuilder()
            .SetBasePath(baseDirectory);

        foreach (var pluginConfigPath in FindPluginDefaultConfigurationFiles(baseDirectory, bootstrapConfiguration))
        {
            configuration.AddJsonFile(pluginConfigPath, optional: true, reloadOnChange: false);
        }

        configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);

        if (packagedMachineProfilePath is not null)
        {
            configuration.AddJsonFile(packagedMachineProfilePath, optional: true, reloadOnChange: true);
        }

        if (externalMachineProfilePath is not null)
        {
            configuration.AddJsonFile(externalMachineProfilePath, optional: true, reloadOnChange: true);
        }

        configuration
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shell:Environment"] = environmentName,
                ["Shell:MachineProfile"] = machineProfile,
                ["Shell:MachineProfileFileName"] = machineProfileFileName,
                ["Shell:MachineProfileLoaded"] = machineProfileLoaded.ToString(),
                ["Shell:MachineProfilePath"] = effectiveMachineProfilePath,
                ["Shell:ExternalMachineProfilePath"] = externalMachineProfilePath,
                ["Shell:ExternalMachineProfileLoaded"] = externalMachineProfileLoaded.ToString()
            });

        return new ShellConfigurationLoadResult(
            configuration.Build(),
            environmentName,
            machineProfile,
            machineProfileFileName,
            machineProfileLoaded)
        {
            MachineProfilePath = effectiveMachineProfilePath,
            ExternalMachineProfilePath = externalMachineProfilePath,
            IsExternalMachineProfileLoaded = externalMachineProfileLoaded
        };
    }

    private string GetEnvironmentName()
        => Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

    private IReadOnlyList<string> FindPluginDefaultConfigurationFiles(string baseDirectory, IConfiguration configuration)
    {
        var configuredRoots = configuration
            .GetSection("Modules:PluginRoots")
            .Get<string[]>()
            ?? [];
        var pluginRoots = configuredRoots
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(path => ResolveConfiguredPluginRoot(baseDirectory, path))
            .ToList();
        if (pluginRoots.Count == 0)
        {
            pluginRoots.Add(EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(baseDirectory));
        }

        return pluginRoots
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(static (pluginRoot, rootIndex) => Directory
                .GetFiles(pluginRoot, "*.module.json", SearchOption.AllDirectories)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new { Path = path, RootIndex = rootIndex }))
            .OrderBy(static item => item.RootIndex)
            .ThenBy(static item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(static item => item.Path)
            .ToArray();
    }

    private static string ResolveConfiguredPluginRoot(string baseDirectory, string path)
        => EdgeClientProgramDataPaths.ResolveConfiguredPluginRoot(baseDirectory, path);

    private static void TryInitializeExternalMachineProfile(string? sourcePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)
            || !File.Exists(sourcePath)
            || File.Exists(targetPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(sourcePath, targetPath, overwrite: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
