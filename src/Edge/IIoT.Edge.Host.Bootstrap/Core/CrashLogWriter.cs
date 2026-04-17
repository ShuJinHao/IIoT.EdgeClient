using System.IO;
using System.Text;

namespace IIoT.Edge.Shell.Core;

public static class CrashLogWriter
{
    private static readonly object Sync = new();

    public static string LogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");

    public static void Write(string source, Exception? exception = null, string? details = null)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath) ?? AppDomain.CurrentDomain.BaseDirectory);

                using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, Encoding.UTF8);

                writer.WriteLine($"[{DateTime.Now:O}] {source}");
                if (!string.IsNullOrWhiteSpace(details))
                {
                    writer.WriteLine(details);
                }

                if (exception is not null)
                {
                    writer.WriteLine(exception);
                }

                writer.WriteLine();
            }
        }
        catch
        {
        }
    }
}
