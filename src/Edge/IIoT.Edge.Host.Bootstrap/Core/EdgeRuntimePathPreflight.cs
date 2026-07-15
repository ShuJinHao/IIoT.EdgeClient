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
        => EnsureWritable(runtimePaths, ProbeWriteAndReplace);

    internal static EdgeRuntimePathPreflightResult EnsureWritable(
        EdgeRuntimePaths runtimePaths,
        Func<string, Exception?> directoryProbe)
    {
        ArgumentNullException.ThrowIfNull(runtimePaths);
        ArgumentNullException.ThrowIfNull(directoryProbe);

        if (TryPrepareRuntimeDirectories(
                runtimePaths,
                directoryProbe,
                out var failedPath,
                out var primaryException))
        {
            return new EdgeRuntimePathPreflightResult(runtimePaths, []);
        }

        var fallbackPaths = CreateFallbackRuntimePaths(runtimePaths);
        if (TryPrepareRuntimeDirectories(fallbackPaths, directoryProbe, out _, out _))
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

        TryPrepareRuntimeDirectories(
            fallbackPaths,
            directoryProbe,
            out var fallbackFailedPath,
            out var fallbackException);
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

    private static bool TryPrepareRuntimeDirectories(
        EdgeRuntimePaths runtimePaths,
        Func<string, Exception?> directoryProbe,
        out string? failedPath,
        out Exception? exception)
    {
        foreach (var directory in EnumerateRuntimeDirectories(runtimePaths))
        {
            try
            {
                Directory.CreateDirectory(directory);
                var probeFailure = directoryProbe(directory);
                if (probeFailure is not null)
                    throw probeFailure;
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

    private static Exception? ProbeWriteAndReplace(string directory)
    {
        var probeId = Guid.NewGuid().ToString("N");
        var sourcePath = Path.Combine(directory, $".iiot-edge-write-probe-{probeId}.tmp");
        var targetPath = Path.Combine(directory, $".iiot-edge-write-probe-{probeId}.replace");
        Exception? failure = null;
        try
        {
            WriteProbeFile(sourcePath, 0x31);
            WriteProbeFile(targetPath, 0x32);
            File.Move(sourcePath, targetPath, overwrite: true);
            if (File.ReadAllBytes(targetPath) is not [0x31])
                throw new IOException($"运行目录原子替换验证失败：{directory}");
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            failure ??= TryDeleteProbe(sourcePath);
            failure ??= TryDeleteProbe(targetPath);
        }

        return failure;
    }

    private static void WriteProbeFile(string path, byte value)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1,
            FileOptions.WriteThrough);
        stream.WriteByte(value);
        stream.Flush(flushToDisk: true);
    }

    private static Exception? TryDeleteProbe(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
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
