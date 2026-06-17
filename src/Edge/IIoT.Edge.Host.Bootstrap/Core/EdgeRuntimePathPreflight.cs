using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.SharedKernel.Configuration;
using System.IO;

namespace IIoT.Edge.Shell.Core;

public sealed record EdgeRuntimePathPreflightResult(
    EdgeRuntimePaths RuntimePaths,
    IReadOnlyList<StartupDiagnosticIssue> Issues);

public static class EdgeRuntimePathPreflight
{
    private const string FallbackDirectoryName = "runtime-fallback";

    public static EdgeRuntimePathPreflightResult EnsureWritable(EdgeRuntimePaths runtimePaths)
    {
        ArgumentNullException.ThrowIfNull(runtimePaths);

        if (TryCreateRuntimeDirectories(runtimePaths, out var failedPath, out var primaryException))
        {
            return new EdgeRuntimePathPreflightResult(runtimePaths, []);
        }

        var fallbackPaths = CreateFallbackRuntimePaths(runtimePaths);
        if (TryCreateRuntimeDirectories(fallbackPaths, out _, out _))
        {
            return new EdgeRuntimePathPreflightResult(
                fallbackPaths,
                [
                    StartupDiagnosticIssueFactory.Create(
                        "RUNTIME_DATA_ROOT_FALLBACK",
                        "运行数据目录不可用，已切换到本机可写备用目录。"
                        + $" 原目录：{runtimePaths.RuntimeDataRoot}；失败路径：{failedPath ?? runtimePaths.RuntimeDataRoot}；"
                        + $"备用目录：{fallbackPaths.RuntimeDataRoot}；原因：{primaryException?.Message ?? "未知错误"}。")
                ]);
        }

        TryCreateRuntimeDirectories(fallbackPaths, out var fallbackFailedPath, out var fallbackException);
        return new EdgeRuntimePathPreflightResult(
            runtimePaths,
            [
                StartupDiagnosticIssueFactory.Create(
                    "RUNTIME_DATA_ROOT_UNAVAILABLE",
                    "运行数据目录和备用目录都不可用，后续持久化服务可能无法启动。"
                    + $" 原目录：{runtimePaths.RuntimeDataRoot}；失败路径：{failedPath ?? runtimePaths.RuntimeDataRoot}；"
                    + $"原错误：{primaryException?.Message ?? "未知错误"}；"
                    + $"备用目录：{fallbackPaths.RuntimeDataRoot}；备用失败路径：{fallbackFailedPath ?? fallbackPaths.RuntimeDataRoot}；"
                    + $"备用错误：{fallbackException?.Message ?? "未知错误"}。")
            ]);
    }

    private static bool TryCreateRuntimeDirectories(
        EdgeRuntimePaths runtimePaths,
        out string? failedPath,
        out Exception? exception)
    {
        foreach (var directory in EnumerateRuntimeDirectories(runtimePaths))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex)
            {
                failedPath = directory;
                exception = ex;
                return false;
            }
        }

        failedPath = null;
        exception = null;
        return true;
    }

    private static IEnumerable<string> EnumerateRuntimeDirectories(EdgeRuntimePaths runtimePaths)
        => new[]
            {
                runtimePaths.RuntimeDataRoot,
                runtimePaths.DatabaseDirectory,
                runtimePaths.ContextDirectory,
                runtimePaths.RecipeDirectory,
                runtimePaths.ExcelDirectory,
                runtimePaths.DiagnosticsDirectory,
                runtimePaths.LogDirectory
            }
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static EdgeRuntimePaths CreateFallbackRuntimePaths(EdgeRuntimePaths runtimePaths)
    {
        var profile = EdgeClientProgramDataPaths.SanitizePathSegment(runtimePaths.ProfileName);
        var fallbackRoot = Path.Combine(
            ResolveWritableFallbackBase(),
            "IIoT.Edge",
            FallbackDirectoryName,
            profile);
        var diagnosticsDirectory = Path.Combine(fallbackRoot, "diagnostics");
        return runtimePaths with
        {
            RuntimeDataRoot = fallbackRoot,
            DatabaseDirectory = Path.Combine(fallbackRoot, "db"),
            ContextDirectory = Path.Combine(fallbackRoot, "context"),
            RecipeDirectory = Path.Combine(fallbackRoot, "recipe"),
            ExcelDirectory = Path.Combine(fallbackRoot, "excel"),
            DiagnosticsDirectory = diagnosticsDirectory,
            LogDirectory = Path.Combine(diagnosticsDirectory, "logs"),
            DeviceCacheFilePath = Path.Combine(fallbackRoot, "device_cache.json"),
            PrimaryCrashLogPath = Path.Combine(diagnosticsDirectory, "crash.log"),
            FallbackCrashLogPath = Path.Combine(diagnosticsDirectory, "crash.fallback.log")
        };
    }

    private static string ResolveWritableFallbackBase()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetTempPath()
            : localAppData;
    }
}
