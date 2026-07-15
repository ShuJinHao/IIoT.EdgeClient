using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.SharedKernel.Configuration;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace IIoT.Edge.Shell.Core;

public sealed record ShellRuntimePathResolutionResult(
    EdgeRuntimePaths RuntimePaths,
    IReadOnlyList<StartupDiagnosticIssue> Issues);

public interface IShellRuntimePathResolver
{
    EdgeRuntimePaths Resolve(string baseDirectory, IConfiguration configuration);

    ShellRuntimePathResolutionResult ResolveWithDiagnostics(
        string baseDirectory,
        IConfiguration configuration);
}

public sealed class ShellRuntimePathResolver : IShellRuntimePathResolver
{
    public EdgeRuntimePaths Resolve(string baseDirectory, IConfiguration configuration)
        => ResolveWithDiagnostics(baseDirectory, configuration).RuntimePaths;

    public ShellRuntimePathResolutionResult ResolveWithDiagnostics(
        string baseDirectory,
        IConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(configuration);

        var issues = new List<StartupDiagnosticIssue>();
        var normalizedBaseDirectory = Path.GetFullPath(baseDirectory);
        var requestedMachineProfile = configuration["Shell:MachineProfile"]?.Trim();
        var profileName = EdgeClientProgramDataPaths.SanitizePathSegment(
            string.IsNullOrWhiteSpace(requestedMachineProfile)
                ? "Default"
                : requestedMachineProfile);
        if (!string.IsNullOrWhiteSpace(requestedMachineProfile)
            && !string.Equals(profileName, requestedMachineProfile, StringComparison.Ordinal))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "RUNTIME_PROFILE_NAME_SANITIZED",
                $"运行目录的机型名称包含不安全字符，已使用安全名称“{profileName}”。"));
        }

        string defaultRuntimeDataRoot;
        try
        {
            defaultRuntimeDataRoot = EdgeClientProgramDataPaths.ResolveProfileDataRoot(
                profileName,
                normalizedBaseDirectory);
        }
        catch (Exception ex)
        {
            defaultRuntimeDataRoot = Path.Combine(
                normalizedBaseDirectory,
                "runtime-data",
                profileName);
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "RUNTIME_DEFAULT_ROOT_INVALID",
                $"默认运行数据根目录无法解析，已回退到宿主目录内的安全路径：{ex.Message}"));
        }

        var runtimeDataRootSetting = configuration["Shell:RuntimeDataRoot"]?.Trim();
        var runtimeDataRoot = defaultRuntimeDataRoot;
        if (!string.IsNullOrWhiteSpace(runtimeDataRootSetting))
        {
            try
            {
                runtimeDataRoot = ResolvePath(normalizedBaseDirectory, runtimeDataRootSetting);
            }
            catch (Exception ex)
            {
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    "RUNTIME_DATA_ROOT_INVALID",
                    $"运行数据根目录配置无效，已回退到 profile 默认目录：{ex.Message}"));
            }
        }

        var diagnosticsDirectory = Path.Combine(runtimeDataRoot, "diagnostics");
        var logDirectory = Path.Combine(diagnosticsDirectory, "logs");
        string fallbackCrashLogPath;
        try
        {
            fallbackCrashLogPath = EdgeClientProgramDataPaths.ResolveProfileFallbackCrashLogPath(
                profileName,
                normalizedBaseDirectory);
        }
        catch (Exception ex)
        {
            fallbackCrashLogPath = Path.Combine(diagnosticsDirectory, "crash.fallback.log");
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "RUNTIME_FALLBACK_CRASH_PATH_INVALID",
                $"备用 crash 日志路径无法解析，已回退到当前运行目录：{ex.Message}"));
        }

        var runtimePaths = new EdgeRuntimePaths(
            BaseDirectory: normalizedBaseDirectory,
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
            FallbackCrashLogPath: fallbackCrashLogPath);
        return new ShellRuntimePathResolutionResult(runtimePaths, issues);
    }

    private string ResolvePath(string baseDirectory, string path)
    {
        if (path.Contains('\0'))
        {
            throw new ArgumentException(
                "Configured runtime data root contains a null character.",
                nameof(path));
        }

        var expanded = EdgeClientProgramDataPaths.ExpandProgramDataTokens(path, baseDirectory);
        if (expanded.Contains('\0'))
        {
            throw new ArgumentException(
                "Expanded runtime data root contains a null character.",
                nameof(path));
        }

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
