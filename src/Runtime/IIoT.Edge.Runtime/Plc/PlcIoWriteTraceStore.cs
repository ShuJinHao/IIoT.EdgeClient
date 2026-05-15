using IIoT.Edge.Application.Abstractions.Plc.Diagnostics;

namespace IIoT.Edge.Runtime.Plc;

/// <summary>
/// 内存 PLC I/O 写入轨迹存储，供 Avalonia 现场联调读取最近写入证据。
/// </summary>
public sealed class PlcIoWriteTraceStore : IPlcIoWriteTraceStore
{
    private const int MaxEntries = 200;
    private readonly object _syncRoot = new();
    private readonly LinkedList<PlcIoWriteTraceEntry> _entries = [];

    public void Record(PlcIoWriteTraceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_syncRoot)
        {
            _entries.AddFirst(CloneEntry(entry));
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveLast();
            }
        }
    }

    public IReadOnlyList<PlcIoWriteTraceEntry> GetRecent(int count = 50)
    {
        lock (_syncRoot)
        {
            return _entries.Take(Math.Max(1, count)).ToArray();
        }
    }

    public PlcIoWriteTraceEntry? GetLatestForSignals(int deviceId, IReadOnlyCollection<string> signalKeys)
    {
        if (signalKeys.Count == 0)
        {
            return null;
        }

        var keys = signalKeys
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keys.Count == 0)
        {
            return null;
        }

        lock (_syncRoot)
        {
            return _entries.FirstOrDefault(entry =>
                entry.DeviceId == deviceId &&
                entry.SignalKeys.Any(keys.Contains));
        }
    }

    private static PlcIoWriteTraceEntry CloneEntry(PlcIoWriteTraceEntry entry)
        => entry with
        {
            SignalKeys = Array.AsReadOnly(entry.SignalKeys
                .Where(static key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray())
        };
}
