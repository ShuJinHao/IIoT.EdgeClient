namespace IIoT.Edge.SharedKernel.Configuration;

public static class EdgeClientProgramDataPaths
{
    public const string ProgramDataRootEnvironmentVariable = "IIOT_EDGE_PROGRAM_DATA_ROOT";
    public const string DataDirectoryName = "data";
    public const string CompanyDirectoryName = "IIoT";
    public const string EdgeClientDirectoryName = "EdgeClient";
    public const string EdgeDataDirectoryName = "EdgeData";
    public const string LauncherDirectoryName = "launcher";
    public const string ProfilesDirectoryName = "profiles";
    public const string PluginsDirectoryName = "plugins";
    public const string PluginCurrentDirectoryName = "current";
    public const string PluginPreviousDirectoryName = "previous";
    public const string DiagnosticsDirectoryName = "diagnostics";
    public const string LogsDirectoryName = "logs";
    public const string LauncherAccountsFileName = "launcher.accounts.json";
    public const string LauncherUpdateConfigFileName = "launcher.update.json";
    public const string LanguageFileName = "language.json";

    public static string ResolveApplicationDataRoot(string? baseDirectory = null)
    {
        var overrideRoot = Environment.GetEnvironmentVariable(ProgramDataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return NormalizeToFullPath(overrideRoot);
        }

        return Path.Combine(ResolveApplicationLayoutRoot(baseDirectory), DataDirectoryName);
    }

    public static string ResolveProgramDataRoot(string? baseDirectory = null)
        => ResolveApplicationDataRoot(baseDirectory);

    public static string ResolveConfigRoot(string? baseDirectory = null)
        => Path.Combine(ResolveApplicationDataRoot(baseDirectory), CompanyDirectoryName, EdgeClientDirectoryName);

    public static string ResolveDataRoot(string? baseDirectory = null)
        => Path.Combine(ResolveApplicationDataRoot(baseDirectory), CompanyDirectoryName, EdgeDataDirectoryName);

    public static string ResolveLauncherDirectory(string? baseDirectory = null)
        => Path.Combine(ResolveConfigRoot(baseDirectory), LauncherDirectoryName);

    public static string ResolveLauncherAccountsPath(string? baseDirectory = null)
        => Path.Combine(ResolveLauncherDirectory(baseDirectory), LauncherAccountsFileName);

    public static string ResolveLauncherLanguagePath(string? baseDirectory = null)
        => Path.Combine(ResolveLauncherDirectory(baseDirectory), LanguageFileName);

    public static string ResolveLauncherUpdateConfigPath(string? baseDirectory = null)
        => Path.Combine(ResolveLauncherDirectory(baseDirectory), LauncherUpdateConfigFileName);

    public static string ResolveProfileConfigDirectory(string profileName, string? baseDirectory = null)
        => Path.Combine(ResolveConfigRoot(baseDirectory), ProfilesDirectoryName, SanitizePathSegment(profileName));

    public static string ResolveMachineProfileConfigPath(string profileName, string? baseDirectory = null)
    {
        var profile = SanitizePathSegment(profileName);
        return Path.Combine(
            ResolveProfileConfigDirectory(profile, baseDirectory),
            $"appsettings.machine.{profile}.json");
    }

    public static string ResolveProfilePluginRootPath(string profileName, string? baseDirectory = null)
        => Path.Combine(ResolveProfileConfigDirectory(profileName, baseDirectory), PluginsDirectoryName);

    public static string ResolveProfilePluginDirectory(
        string profileName,
        string moduleId,
        string? baseDirectory = null)
        => Path.Combine(
            ResolveProfilePluginRootPath(profileName, baseDirectory),
            SanitizePathSegment(moduleId));

    public static string ResolveProfilePluginCurrentDirectory(
        string profileName,
        string moduleId,
        string? baseDirectory = null)
        => Path.Combine(
            ResolveProfilePluginDirectory(profileName, moduleId, baseDirectory),
            PluginCurrentDirectoryName);

    public static string ResolveProfilePluginPreviousDirectory(
        string profileName,
        string moduleId,
        string? baseDirectory = null)
        => Path.Combine(
            ResolveProfilePluginDirectory(profileName, moduleId, baseDirectory),
            PluginPreviousDirectoryName);

    public static string ResolveProfileDataRoot(string profileName, string? baseDirectory = null)
        => Path.Combine(ResolveDataRoot(baseDirectory), ProfilesDirectoryName, SanitizePathSegment(profileName));

    public static string ResolveProfileFallbackCrashLogPath(string profileName, string? baseDirectory = null)
        => Path.Combine(
            ResolveProfileDataRoot(profileName, baseDirectory),
            DiagnosticsDirectoryName,
            "crash.fallback.log");

    public static string ExpandProgramDataTokens(string path, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded.Contains("%ProgramData%", StringComparison.OrdinalIgnoreCase))
        {
            expanded = expanded.Replace(
                "%ProgramData%",
                ResolveApplicationDataRoot(baseDirectory),
                StringComparison.OrdinalIgnoreCase);
        }

        return expanded;
    }

    public static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Default";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized)
            ? "Default"
            : sanitized;
    }

    private static string NormalizeToFullPath(string path)
        => Path.GetFullPath(
            path
                .Trim()
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar));

    private static string ResolveApplicationLayoutRoot(string? baseDirectory)
    {
        var normalizedBaseDirectory = NormalizeToFullPath(
            string.IsNullOrWhiteSpace(baseDirectory)
                ? AppContext.BaseDirectory
                : baseDirectory);
        var directory = new DirectoryInfo(normalizedBaseDirectory);

        if (IsVelopackCurrentDirectory(directory))
        {
            return directory.Parent!.FullName;
        }

        if (directory.Parent is not null && IsVelopackCurrentDirectory(directory.Parent))
        {
            return directory.Parent.Parent!.FullName;
        }

        return directory.Parent?.FullName ?? directory.FullName;
    }

    private static bool IsVelopackCurrentDirectory(DirectoryInfo directory)
        => string.Equals(directory.Name, "current", StringComparison.OrdinalIgnoreCase)
            && directory.Parent is not null;
}
