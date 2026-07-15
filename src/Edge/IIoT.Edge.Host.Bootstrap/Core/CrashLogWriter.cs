using System.Diagnostics;
using System.IO;
using System.Text;

namespace IIoT.Edge.Shell.Core;

public interface ICrashLogWriter
{
    string LogPath { get; }

    void ConfigurePaths(string primaryLogPath, string fallbackLogPath);

    void Write(string source, Exception? exception = null, string? details = null);
}

public sealed class CrashLogWriter : ICrashLogWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _sync = new();
    private readonly Func<string> _defaultPrimaryLogPathProvider;
    private readonly Func<string> _defaultFallbackLogPathProvider;
    private readonly Action<string, string> _appendEntryToPath;
    private readonly Action<string> _diagnosticSink;
    private string? _primaryLogPath;
    private string? _fallbackLogPath;

    public CrashLogWriter()
        : this(
            () => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
            () => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IIoT.Edge",
                "diagnostics",
                "crash.fallback.log"),
            AppendEntryToPathCore,
            WriteToDiagnosticSinkCore)
    {
    }

    internal CrashLogWriter(
        Func<string> defaultPrimaryLogPathProvider,
        Func<string> defaultFallbackLogPathProvider,
        Action<string, string> appendEntryToPath,
        Action<string> diagnosticSink)
    {
        _defaultPrimaryLogPathProvider = defaultPrimaryLogPathProvider
            ?? throw new ArgumentNullException(nameof(defaultPrimaryLogPathProvider));
        _defaultFallbackLogPathProvider = defaultFallbackLogPathProvider
            ?? throw new ArgumentNullException(nameof(defaultFallbackLogPathProvider));
        _appendEntryToPath = appendEntryToPath ?? throw new ArgumentNullException(nameof(appendEntryToPath));
        _diagnosticSink = diagnosticSink ?? throw new ArgumentNullException(nameof(diagnosticSink));
    }

    public string LogPath => _primaryLogPath ?? _defaultPrimaryLogPathProvider();

    private string FallbackLogPath => _fallbackLogPath ?? _defaultFallbackLogPathProvider();

    public void ConfigurePaths(string primaryLogPath, string fallbackLogPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryLogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackLogPath);

        lock (_sync)
        {
            _primaryLogPath = primaryLogPath;
            _fallbackLogPath = fallbackLogPath;
        }
    }

    public void Write(string source, Exception? exception = null, string? details = null)
    {
        lock (_sync)
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

            _diagnosticSink(BuildDiagnosticMessage(
                source,
                exception,
                details,
                primaryPath,
                primaryError!,
                fallbackPath,
                fallbackError!));
        }
    }

    private bool TryWrite(string path, string entry, out Exception? error)
    {
        try
        {
            _appendEntryToPath(path, entry);
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
        Exception? consoleError = null;
        try
        {
            Console.Error.WriteLine(message);
        }
        catch (Exception ex)
        {
            consoleError = ex;
        }

        if (consoleError is not null)
        {
            Trace.WriteLine($"[CrashLogFallback] console_error={consoleError}");
        }

        try
        {
            Trace.WriteLine(message);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Crash log diagnostic sink failed after primary and fallback crash log writes failed.", ex);
        }
    }
}
