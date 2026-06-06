namespace IIoT.Edge.SharedKernel.Configuration;

public static class EdgeClientProgramDataPaths
{
    public const string ProgramDataRootEnvironmentVariable = "IIOT_EDGE_PROGRAM_DATA_ROOT";
    public const string CompanyDirectoryName = "IIoT";
    public const string EdgeClientDirectoryName = "EdgeClient";
    public const string EdgeDataDirectoryName = "EdgeData";
    public const string LauncherDirectoryName = "launcher";
    public const string ProfilesDirectoryName = "profiles";
    public const string DiagnosticsDirectoryName = "diagnostics";
    public const string LogsDirectoryName = "logs";
    public const string LauncherAccountsFileName = "launcher.accounts.json";
    public const string LauncherUpdateConfigFileName = "launcher.update.json";
    public const string LanguageFileName = "language.json";

    public static string ResolveProgramDataRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(ProgramDataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return NormalizeToFullPath(overrideRoot);
        }

        var commonApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(commonApplicationData))
        {
            return NormalizeToFullPath(commonApplicationData);
        }

        return NormalizeToFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProgramData"));
    }

    public static string ResolveConfigRoot()
        => Path.Combine(ResolveProgramDataRoot(), CompanyDirectoryName, EdgeClientDirectoryName);

    public static string ResolveDataRoot()
        => Path.Combine(ResolveProgramDataRoot(), CompanyDirectoryName, EdgeDataDirectoryName);

    public static string ResolveLauncherDirectory()
        => Path.Combine(ResolveConfigRoot(), LauncherDirectoryName);

    public static string ResolveLauncherAccountsPath()
        => Path.Combine(ResolveLauncherDirectory(), LauncherAccountsFileName);

    public static string ResolveLauncherLanguagePath()
        => Path.Combine(ResolveLauncherDirectory(), LanguageFileName);

    public static string ResolveLauncherUpdateConfigPath()
        => Path.Combine(ResolveLauncherDirectory(), LauncherUpdateConfigFileName);

    public static string ResolveProfileConfigDirectory(string profileName)
        => Path.Combine(ResolveConfigRoot(), ProfilesDirectoryName, SanitizePathSegment(profileName));

    public static string ResolveMachineProfileConfigPath(string profileName)
    {
        var profile = SanitizePathSegment(profileName);
        return Path.Combine(ResolveProfileConfigDirectory(profile), $"appsettings.machine.{profile}.json");
    }

    public static string ResolveProfileDataRoot(string profileName)
        => Path.Combine(ResolveDataRoot(), ProfilesDirectoryName, SanitizePathSegment(profileName));

    public static string ResolveProfileFallbackCrashLogPath(string profileName)
        => Path.Combine(
            ResolveProfileDataRoot(profileName),
            DiagnosticsDirectoryName,
            "crash.fallback.log");

    public static string ExpandProgramDataTokens(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded.Contains("%ProgramData%", StringComparison.OrdinalIgnoreCase))
        {
            expanded = expanded.Replace("%ProgramData%", ResolveProgramDataRoot(), StringComparison.OrdinalIgnoreCase);
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
}
