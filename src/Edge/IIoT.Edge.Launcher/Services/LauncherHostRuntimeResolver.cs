using System.Text.Json;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

internal sealed record LauncherHostRuntimeLocation(
    string ExecutablePath,
    string HostDirectory);

internal sealed class LauncherHostRuntimeResolver(
    string baseDirectory,
    string catalogFileName = "launcher.profiles.json")
{
    private static readonly string DefaultExecutablePath =
        Path.Combine("..", "host", "IIoT.Edge.Shell");
    private readonly string _catalogPath = Path.Combine(baseDirectory, catalogFileName);

    public LauncherHostRuntimeLocation Resolve()
    {
        if (!File.Exists(_catalogPath))
        {
            var fallbackExecutable = Path.GetFullPath(Path.Combine(
                baseDirectory,
                DefaultExecutablePath));
            return new LauncherHostRuntimeLocation(
                fallbackExecutable,
                Path.GetDirectoryName(fallbackExecutable) ?? baseDirectory);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(_catalogPath));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("启动器工序清单必须是数组。");
        }

        var entries = document.RootElement.EnumerateArray().ToArray();
        if (entries.Length == 0)
        {
            throw new InvalidOperationException("启动器工序清单为空。");
        }

        var hostEntry = entries.FirstOrDefault(static entry =>
            string.Equals(
                ReadString(entry, "ProfileId"),
                "Default",
                StringComparison.OrdinalIgnoreCase));
        if (hostEntry.ValueKind == JsonValueKind.Undefined)
        {
            hostEntry = entries[0];
        }

        var configuredPath = ReadString(hostEntry, "ExecutablePath");
        var expanded = EdgeClientProgramDataPaths.ExpandProgramDataTokens(
                string.IsNullOrWhiteSpace(configuredPath)
                    ? DefaultExecutablePath
                    : configuredPath,
                baseDirectory)
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var executablePath = Path.GetFullPath(
            Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(baseDirectory, expanded));
        return new LauncherHostRuntimeLocation(
            executablePath,
            Path.GetDirectoryName(executablePath) ?? baseDirectory);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString()?.Trim();
            }
        }

        return null;
    }
}
