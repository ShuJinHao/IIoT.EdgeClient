using IIoT.Edge.Application.Abstractions.Plc.Store;
using System.Collections.Concurrent;
using System.Threading;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;

public class PlcBuffer : IPlcBufferTransport
{
    private ushort[] _readBuffer;
    private readonly ushort[] _writeBuffer;
    private readonly object _writeSync = new();
    private readonly object _bindingSync = new();
    private ushort[] _writeSnapshot;
    private bool _writeSnapshotDirty = true;
    private IReadOnlyDictionary<string, PlcBufferSignalBinding> _readBindings =
        new Dictionary<string, PlcBufferSignalBinding>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, PlcBufferSignalBinding> _writeBindings =
        new Dictionary<string, PlcBufferSignalBinding>(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ushort[]> _readSignals = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ushort[]> _writeSignals = new(StringComparer.OrdinalIgnoreCase);

    public PlcBuffer(
        int readSize,
        int writeSize,
        IReadOnlyCollection<PlcBufferSignalBinding>? signalBindings = null)
    {
        _readBuffer = new ushort[Math.Max(0, readSize)];
        _writeBuffer = new ushort[Math.Max(0, writeSize)];
        _writeSnapshot = new ushort[Math.Max(0, writeSize)];
        SetSignalBindings(signalBindings ?? []);
    }

    public event EventHandler<PlcSignalBufferChangedEventArgs>? SignalValuesChanged;

    public ushort GetReadValue(int index)
    {
        var snapshot = Volatile.Read(ref _readBuffer);
        return index >= 0 && index < snapshot.Length ? snapshot[index] : (ushort)0;
    }

    public bool TryGetReadWords(string signalKey, out ushort[] values)
    {
        if (_readSignals.TryGetValue(signalKey, out var cached))
        {
            values = (ushort[])cached.Clone();
            return true;
        }

        if (TryGetBinding(_readBindings, signalKey, out var binding))
        {
            values = ReadWordsFromReadBuffer(binding);
            return true;
        }

        values = [];
        return false;
    }

    public bool TryGetWriteWords(string signalKey, out ushort[] values)
    {
        if (_writeSignals.TryGetValue(signalKey, out var cached))
        {
            values = (ushort[])cached.Clone();
            return true;
        }

        if (TryGetBinding(_writeBindings, signalKey, out var binding))
        {
            lock (_writeSync)
            {
                values = ReadWords(_writeBuffer, binding.Offset, binding.AddressCount);
            }

            return true;
        }

        values = [];
        return false;
    }

    public void SetWriteValue(int index, ushort value)
    {
        PlcBufferSignalBinding[] affected;

        lock (_writeSync)
        {
            if (index < 0 || index >= _writeBuffer.Length)
            {
                return;
            }

            _writeBuffer[index] = value;
            _writeSnapshotDirty = true;
            affected = _writeBindings.Values
                .Where(binding => index >= binding.Offset && index < binding.Offset + binding.AddressCount)
                .ToArray();

            foreach (var binding in affected)
            {
                _writeSignals[binding.SignalKey] = ReadWords(_writeBuffer, binding.Offset, binding.AddressCount);
            }
        }

        foreach (var binding in affected)
        {
            NotifyChanged(binding.SignalKey, "Write");
        }
    }

    public void SetWriteValue(string signalKey, int offset, ushort value)
    {
        if (!TryGetBinding(_writeBindings, signalKey, out var binding))
        {
            if (offset < 0)
            {
                return;
            }

            lock (_writeSync)
            {
                var words = _writeSignals.TryGetValue(signalKey, out var existing) && existing.Length > offset
                    ? (ushort[])existing.Clone()
                    : new ushort[offset + 1];

                if (_writeSignals.TryGetValue(signalKey, out existing))
                {
                    Array.Copy(existing, words, Math.Min(existing.Length, words.Length));
                }

                words[offset] = value;
                _writeSignals[signalKey] = words;
            }

            NotifyChanged(signalKey, "Write");
            return;
        }

        if (offset < 0 || offset >= binding.AddressCount)
        {
            return;
        }

        lock (_writeSync)
        {
            var words = _writeSignals.TryGetValue(signalKey, out var existing)
                ? (ushort[])existing.Clone()
                : ReadWords(_writeBuffer, binding.Offset, binding.AddressCount);

            words[offset] = value;
            _writeSignals[signalKey] = words;

            var bufferIndex = binding.Offset + offset;
            if (bufferIndex >= 0 && bufferIndex < _writeBuffer.Length)
            {
                _writeBuffer[bufferIndex] = value;
                _writeSnapshotDirty = true;
            }
        }

        NotifyChanged(signalKey, "Write");
    }

    public void UpdateReadBuffer(ushort[] data)
    {
        var next = new ushort[_readBuffer.Length];
        Array.Copy(data, next, Math.Min(data.Length, next.Length));
        Interlocked.Exchange(ref _readBuffer, next);

        foreach (var binding in _readBindings.Values)
        {
            _readSignals[binding.SignalKey] = ReadWords(next, binding.Offset, binding.AddressCount);
            NotifyChanged(binding.SignalKey, "Read");
        }
    }

    public void UpdateReadSignal(string signalKey, IReadOnlyList<ushort> data)
    {
        var words = NormalizeWords(data);
        _readSignals[signalKey] = words;

        if (TryGetBinding(_readBindings, signalKey, out var binding))
        {
            var next = (ushort[])Volatile.Read(ref _readBuffer).Clone();
            for (var index = 0; index < Math.Min(words.Length, binding.AddressCount); index++)
            {
                var bufferIndex = binding.Offset + index;
                if (bufferIndex >= 0 && bufferIndex < next.Length)
                {
                    next[bufferIndex] = words[index];
                }
            }

            Interlocked.Exchange(ref _readBuffer, next);
        }

        NotifyChanged(signalKey, "Read");
    }

    public ushort[] GetWriteBuffer()
    {
        lock (_writeSync)
        {
            if (_writeSnapshotDirty || _writeSnapshot.Length != _writeBuffer.Length)
            {
                _writeSnapshot = (ushort[])_writeBuffer.Clone();
                _writeSnapshotDirty = false;
            }

            return (ushort[])_writeSnapshot.Clone();
        }
    }

    public void SetSignalBindings(IReadOnlyCollection<PlcBufferSignalBinding> bindings)
    {
        lock (_bindingSync)
        {
            _readBindings = bindings
                .Where(static binding => string.Equals(binding.Direction, "Read", StringComparison.OrdinalIgnoreCase))
                .GroupBy(static binding => binding.SignalKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

            _writeBindings = bindings
                .Where(static binding => string.Equals(binding.Direction, "Write", StringComparison.OrdinalIgnoreCase))
                .GroupBy(static binding => binding.SignalKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool Matches(int readSize, int writeSize)
    {
        var readLength = Volatile.Read(ref _readBuffer).Length;
        return readLength == Math.Max(0, readSize) && _writeBuffer.Length == Math.Max(0, writeSize);
    }

    private ushort[] ReadWordsFromReadBuffer(PlcBufferSignalBinding binding)
        => ReadWords(Volatile.Read(ref _readBuffer), binding.Offset, binding.AddressCount);

    private static ushort[] ReadWords(IReadOnlyList<ushort> source, int offset, int count)
    {
        var words = new ushort[Math.Max(1, count)];
        for (var index = 0; index < words.Length; index++)
        {
            var sourceIndex = offset + index;
            words[index] = sourceIndex >= 0 && sourceIndex < source.Count ? source[sourceIndex] : (ushort)0;
        }

        return words;
    }

    private static ushort[] NormalizeWords(IReadOnlyList<ushort> data)
    {
        if (data.Count == 0)
        {
            return [];
        }

        var words = new ushort[data.Count];
        for (var index = 0; index < data.Count; index++)
        {
            words[index] = data[index];
        }

        return words;
    }

    private static bool TryGetBinding(
        IReadOnlyDictionary<string, PlcBufferSignalBinding> bindings,
        string signalKey,
        out PlcBufferSignalBinding binding)
        => bindings.TryGetValue(signalKey, out binding!);

    private void NotifyChanged(string signalKey, string direction)
        => SignalValuesChanged?.Invoke(this, new PlcSignalBufferChangedEventArgs(signalKey, direction));
}
