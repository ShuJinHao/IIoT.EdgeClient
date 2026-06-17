using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.SharedKernel.Configuration;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace IIoT.Edge.Shell.Core;

public interface IShellRuntimePathResolver
{
    EdgeRuntimePaths Resolve(string baseDirectory, IConfiguration configuration);
}

public sealed class ShellRuntimePathResolver : IShellRuntimePathResolver
{
    public EdgeRuntimePaths Resolve(string baseDirectory, IConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(configuration);

        var machineProfile = configuration["Shell:MachineProfile"]?.Trim();
        var profileName = string.IsNullOrWhiteSpace(machineProfile)
            ? "Default"
            : machineProfile;
        var runtimeDataRootSetting = configuration["Shell:RuntimeDataRoot"]?.Trim();
        var runtimeDataRoot = string.IsNullOrWhiteSpace(runtimeDataRootSetting)
            ? EdgeClientProgramDataPaths.ResolveProfileDataRoot(profileName, baseDirectory)
            : ResolvePath(baseDirectory, runtimeDataRootSetting);
        var diagnosticsDirectory = Path.Combine(runtimeDataRoot, "diagnostics");
        var logDirectory = Path.Combine(diagnosticsDirectory, "logs");

        return new EdgeRuntimePaths(
            BaseDirectory: baseDirectory,
            ProfileName: profileName,
            RuntimeDataRoot: runtimeDataRoot,
            DatabaseDirectory: Path.Combine(runtimeDataRoot, "db"),
            ContextDirectory: Path.Combine(runtimeDataRoot, "context"),
            RecipeDirectory: Path.Combine(runtimeDataRoot, "recipe"),
            ExcelDirectory: Path.Combine(runtimeDataRoot, "excel"),
            DiagnosticsDirectory: diagnosticsDirectory,
            LogDirectory: logDirectory,
            DeviceCacheFilePath: Path.Combine(runtimeDataRoot, "device_cache.json"),
            PrimaryCrashLogPath: Path.Combine(diagnosticsDirectory, "crash.log"),
            FallbackCrashLogPath: EdgeClientProgramDataPaths.ResolveProfileFallbackCrashLogPath(profileName, baseDirectory));
    }

    private string ResolvePath(string baseDirectory, string path)
    {
        var expanded = EdgeClientProgramDataPaths.ExpandProgramDataTokens(path, baseDirectory);
        var normalized = NormalizePathSeparators(expanded);
        return Path.GetFullPath(
            Path.IsPathRooted(normalized)
                ? normalized
                : Path.Combine(baseDirectory, normalized));
    }

    private static string NormalizePathSeparators(string path)
        => path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

}
