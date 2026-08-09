using System.Globalization;
using System.Text;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Infrastructure.Integration.EdgeHost;

public interface IPlcConfigurationVersionStore
{
    long ReadOrCreate(string clientCode);

    long Advance(string clientCode);
}

/// <summary>
/// Persists the authoritative PLC configuration version under the device plugin cache. A
/// process restart therefore cannot move the version backwards and an authoritative empty
/// snapshot can only clear Cloud with a newly advanced version.
/// </summary>
public sealed class FilePlcConfigurationVersionStore(string runtimeBaseDirectory)
    : IPlcConfigurationVersionStore
{
    private const string FileName = "plc-configuration.version";
    private readonly object _sync = new();

    public long ReadOrCreate(string clientCode)
    {
        lock (_sync)
        {
            var path = ResolvePath(clientCode);
            if (File.Exists(path))
            {
                return Parse(File.ReadAllText(path));
            }

            var initial = NextCandidate(0);
            WriteAtomically(path, initial);
            return initial;
        }
    }

    public long Advance(string clientCode)
    {
        lock (_sync)
        {
            var path = ResolvePath(clientCode);
            var current = File.Exists(path) ? Parse(File.ReadAllText(path)) : 0;
            var next = NextCandidate(current);
            WriteAtomically(path, next);
            return next;
        }
    }

    private string ResolvePath(string clientCode)
        => Path.Combine(
            EdgeClientProgramDataPaths.ResolveDevicePluginDirectory(
                EdgeClientIdentity.NormalizeClientCode(clientCode),
                "cache",
                runtimeBaseDirectory),
            FileName);

    private static long Parse(string value)
        => long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
           && parsed > 0
            ? parsed
            : throw new InvalidDataException("PLC configuration version file is invalid.");

    private static long NextCandidate(long current)
    {
        var clock = DateTimeOffset.UtcNow.UtcTicks;
        return Math.Max(checked(current + 1), clock);
    }

    private static void WriteAtomically(string path, long version)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("PLC configuration version directory is missing.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                version.ToString(CultureInfo.InvariantCulture),
                new UTF8Encoding(false));
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
