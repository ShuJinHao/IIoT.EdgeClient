using System.Diagnostics;
using System.IO;
using System.Text;

namespace IIoT.Edge.Shell.Core;

public static class CrashLogWriter
{
    private static readonly object Sync = new();
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    internal static Func<string> PrimaryLogPathProvider { get; set; }
        = () => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");

    internal static Func<string> FallbackLogPathProvider { get; set; }
        = () => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IIoT.Edge",
            "diagnostics",
            "crash.fallback.log");

    internal static Action<string, string> AppendEntryToPath { get; set; } = AppendEntryToPathCore;

    internal static Action<string> DiagnosticSink { get; set; } = WriteToDiagnosticSinkCore;

    public static string LogPath => PrimaryLogPathProvider();

    internal static string FallbackLogPath => FallbackLogPathProvider();

    public static void ConfigurePaths(Func<string> primaryLogPathProvider, Func<string> fallbackLogPathProvider)
    {
        PrimaryLogPathProvider = primaryLogPathProvider ?? throw new ArgumentNullException(nameof(primaryLogPathProvider));
        FallbackLogPathProvider = fallbackLogPathProvider ?? throw new ArgumentNullException(nameof(fallbackLogPathProvider));
    }

    public static void Write(string source, Exception? exception = null, string? details = null)
    {
        lock (Sync)
        {
            var primaryPath = LogPath;
            var entry = BuildEntry(source, exception, details);

            if (TryWrite(primaryPath, entry, out var primaryError))
            {
                return;
            }

            var fallbackPath = FallbackLogPath;
            var fallbackEntry = BuildFallbackEntry(
                source,
                exception,
                details,
                primaryPath,
                primaryError!,
                fallbackPath);

            if (TryWrite(fallbackPath, fallbackEntry, out var fallbackError))
            {
                return;
            }

            try
            {
                DiagnosticSink(BuildDiagnosticMessage(
                    source,
                    exception,
                    details,
                    primaryPath,
                    primaryError!,
                    fallbackPath,
                    fallbackError!));
            }
            catch
            {
            }
        }
    }

    internal static void ResetTestHooks()
    {
        PrimaryLogPathProvider = () => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
        FallbackLogPathProvider = () => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IIoT.Edge",
            "diagnostics",
            "crash.fallback.log");
        AppendEntryToPath = AppendEntryToPathCore;
        DiagnosticSink = WriteToDiagnosticSinkCore;
    }

    private static bool TryWrite(string path, string entry, out Exception? error)
    {
        try
        {
            AppendEntryToPath(path, entry);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }

    private static string BuildEntry(string source, Exception? exception, string? details)
    {
        var builder = new StringBuilder();
        AppendEntryBody(builder, source, exception, details);
        return builder.ToString();
    }

    private static string BuildFallbackEntry(
        string source,
        Exception? exception,
        string? details,
        string primaryPath,
        Exception primaryError,
        string fallbackPath)
    {
        var builder = new StringBuilder();
        AppendEntryBody(builder, source, exception, details);
        builder.AppendLine($"[CrashLogFallback] primary_path={primaryPath}");
        builder.AppendLine($"[CrashLogFallback] primary_result=failed primary_error={primaryError.Message}");
        builder.AppendLine($"[CrashLogFallback] fallback_path={fallbackPath}");
        builder.AppendLine("[CrashLogFallback] fallback_result=succeeded");
        builder.AppendLine();
        return builder.ToString();
    }

    private static string BuildDiagnosticMessage(
        string source,
        Exception? exception,
        string? details,
        string primaryPath,
        Exception primaryError,
        string fallbackPath,
        Exception fallbackError)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[CrashLogFallback] primary_result=failed");
        builder.AppendLine($"[CrashLogFallback] primary_path={primaryPath}");
        builder.AppendLine($"[CrashLogFallback] primary_error={primaryError}");
        builder.AppendLine("[CrashLogFallback] fallback_result=failed");
        builder.AppendLine($"[CrashLogFallback] fallback_path={fallbackPath}");
        builder.AppendLine($"[CrashLogFallback] fallback_error={fallbackError}");
        builder.AppendLine();
        AppendEntryBody(builder, source, exception, details);
        return builder.ToString();
    }

    private static void AppendEntryBody(
        StringBuilder builder,
        string source,
        Exception? exception,
        string? details)
    {
        builder.AppendLine($"[{DateTime.Now:O}] {source}");
        if (!string.IsNullOrWhiteSpace(details))
        {
            builder.AppendLine(details);
        }

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        builder.AppendLine();
    }

    private static void AppendEntryToPathCore(string path, string entry)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream, Utf8NoBom);
        writer.Write(entry);
    }

    private static void WriteToDiagnosticSinkCore(string message)
    {
        try
        {
            Console.Error.WriteLine(message);
        }
        catch
        {
        }

        try
        {
            Trace.WriteLine(message);
        }
        catch
        {
        }
    }
}
