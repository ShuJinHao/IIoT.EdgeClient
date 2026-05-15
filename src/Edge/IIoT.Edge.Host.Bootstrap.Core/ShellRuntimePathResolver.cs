using IIoT.Edge.Application.Abstractions.Config;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace IIoT.Edge.Host.Bootstrap.Core;

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
            ? Path.Combine(baseDirectory, "data", "profiles", SanitizePathSegment(profileName))
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
            FallbackCrashLogPath: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IIoT.Edge",
                "profiles",
                SanitizePathSegment(profileName),
                "diagnostics",
                "crash.fallback.log"));
    }

    private string ResolvePath(string baseDirectory, string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.GetFullPath(
            Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(baseDirectory, expanded));
    }

    private string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized)
            ? "Default"
            : sanitized;
    }
}
