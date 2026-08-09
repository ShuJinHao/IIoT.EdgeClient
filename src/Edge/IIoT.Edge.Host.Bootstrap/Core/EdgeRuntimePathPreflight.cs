using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.SharedKernel.Configuration;
using System.IO;

namespace IIoT.Edge.Shell.Core;

public sealed record EdgeRuntimePathPreflightResult(
    EdgeRuntimePaths RuntimePaths,
    IReadOnlyList<StartupDiagnosticIssue> Issues,
    bool Success);

public static class EdgeRuntimePathPreflight
{
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
            return new EdgeRuntimePathPreflightResult(runtimePaths, [], true);
        }

        return new EdgeRuntimePathPreflightResult(
            runtimePaths,
            [
                StartupDiagnosticIssueFactory.Create(
                    "RUNTIME_DATA_ROOT_UNAVAILABLE",
                    "设备插件运行数据目录不可用，已阻止启动；系统不会改用备用目录创建第二份数据库。"
                    + $" 原目录：{runtimePaths.RuntimeDataRoot}；失败路径：{failedPath ?? runtimePaths.RuntimeDataRoot}；"
                    + $"错误：{primaryException?.Message ?? "未知错误"}。")
            ],
            false);
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
            catch (Exception ex) when (StartupExceptionBoundary.IsApprovedPathFailure(ex))
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
        catch (Exception ex) when (StartupExceptionBoundary.IsApprovedPathFailure(ex))
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
        catch (Exception ex) when (StartupExceptionBoundary.IsApprovedPathFailure(ex))
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

}
