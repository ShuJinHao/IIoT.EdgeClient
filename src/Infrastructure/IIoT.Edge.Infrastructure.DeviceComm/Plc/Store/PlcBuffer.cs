using IIoT.Edge.Module.Contracts.Plc.Store;
using System.Collections.Concurrent;
using System.Threading;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;

public class PlcBuffer : IPlcBufferTransport, IPlcReadSnapshotProvider, IPlcReadBatchPublisher
{
    private ushort[] _readBuffer;
    private readonly ushort[] _writeBuffer;
    private readonly object _readSync = new();
    private readonly object _writeSync = new();
    private readonly object _bindingSync = new();
    private ushort[] _writeSnapshot;
    private bool _writeSnapshotDirty = true;
    private IReadOnlyDictionary<string, PlcBufferSignalBinding> _readBindings =
        new Dictionary<string, PlcBufferSignalBinding>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, PlcBufferSignalBinding> _writeBindings =
        new Dictionary<string, PlcBufferSignalBinding>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, PlcReadSignalState> _readSignalStates =
        new Dictionary<string, PlcReadSignalState>(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ushort[]> _writeSignals = new(StringComparer.OrdinalIgnoreCase);
    private long _readGeneration;

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
        var snapshot = Volatile.Read(ref _readSignalStates);
        if (snapshot.TryGetValue(signalKey, out var state))
        {
            values = (ushort[])state.CurrentWords.Clone();
            return state.ReadSucceeded;
        }

        if (TryGetBinding(_readBindings, signalKey, out var binding))
        {
            values = new ushort[Math.Max(1, binding.AddressCount)];
            return false;
        }

        values = [];
        return false;
    }

    public bool TryCaptureReadSnapshot(
        IReadOnlyCollection<string> requiredSignalKeys,
        out PlcReadBatchSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(requiredSignalKeys);
        snapshot = null;
        if (requiredSignalKeys.Count == 0)
        {
            return false;
        }

        var normalizedKeys = new List<string>(requiredSignalKeys.Count);
        var uniqueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var signalKey in requiredSignalKeys)
        {
            if (string.IsNullOrWhiteSpace(signalKey))
            {
                return false;
            }

            var normalizedKey = signalKey.Trim();
            if (!uniqueKeys.Add(normalizedKey))
            {
                return false;
            }

            normalizedKeys.Add(normalizedKey);
        }

        var stateSnapshot = Volatile.Read(ref _readSignalStates);
        var signalSnapshots = new List<PlcReadSignalSnapshot>(normalizedKeys.Count);
        long? generation = null;
        Guid? batchId = null;
        DateTimeOffset? capturedAtUtc = null;
        foreach (var signalKey in normalizedKeys)
        {
            if (!stateSnapshot.TryGetValue(signalKey, out var state)
                || state.Generation <= 0
                || state.BatchId == Guid.Empty)
            {
                return false;
            }

            var stateCapturedAtUtc = state.AttemptedAtUtc.ToUniversalTime();
            if (generation is not null
                && (state.Generation != generation.Value
                    || state.BatchId != batchId
                    || stateCapturedAtUtc != capturedAtUtc))
            {
                return false;
            }

            generation ??= state.Generation;
            batchId ??= state.BatchId;
            capturedAtUtc ??= stateCapturedAtUtc;
            signalSnapshots.Add(
                new PlcReadSignalSnapshot(
                    signalKey,
                    state.Generation,
                    state.BatchId,
                    stateCapturedAtUtc,
                    state.CurrentWords,
                    state.ReadSucceeded,
                    state.FailureReason));
        }

        snapshot = new PlcReadBatchSnapshot(
            generation!.Value,
            batchId!.Value,
            capturedAtUtc!.Value,
            signalSnapshots);
        return true;
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
        PlcBufferSignalBinding[] affected;
        lock (_readSync)
        {
            var next = new ushort[Volatile.Read(ref _readBuffer).Length];
            Array.Copy(data, next, Math.Min(data.Length, next.Length));

            affected = _readBindings.Values.ToArray();
            var batchId = Guid.NewGuid();
            var attemptedAtUtc = DateTimeOffset.UtcNow;
            var generation = NextReadGeneration();
            var nextStates = CopyReadSignalStates();
            foreach (var binding in affected)
            {
                var words = ReadWords(next, binding.Offset, binding.AddressCount);
                nextStates[binding.SignalKey] = CreateNextReadState(
                    nextStates.GetValueOrDefault(binding.SignalKey),
                    generation,
                    new PlcReadSignalUpdate(
                        words,
                        ReadSucceeded: true,
                        batchId,
                        attemptedAtUtc,
                        FailureReason: null));
            }

            Interlocked.Exchange(ref _readBuffer, next);
            Volatile.Write(ref _readSignalStates, nextStates);
        }

        foreach (var binding in affected)
        {
            NotifyChanged(binding.SignalKey, "Read");
        }
    }

    public void UpdateReadSignal(string signalKey, IReadOnlyList<ushort> data)
    {
        PublishReadBatch(
            new Dictionary<string, PlcReadSignalUpdate>(StringComparer.OrdinalIgnoreCase)
            {
                [signalKey] = new(
                    NormalizeWords(data),
                    ReadSucceeded: true,
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    FailureReason: null)
            });
    }

    internal void UpdateReadSignals(IReadOnlyDictionary<string, ushort[]> signalValues)
    {
        ArgumentNullException.ThrowIfNull(signalValues);
        if (signalValues.Count == 0)
        {
            return;
        }

        var batchId = Guid.NewGuid();
        var attemptedAtUtc = DateTimeOffset.UtcNow;
        PublishReadBatch(
            signalValues.ToDictionary(
                static pair => pair.Key,
                pair => new PlcReadSignalUpdate(
                    NormalizeWords(pair.Value),
                    ReadSucceeded: true,
                    batchId,
                    attemptedAtUtc,
                    FailureReason: null),
                StringComparer.OrdinalIgnoreCase));
    }

    void IPlcReadBatchPublisher.PublishReadBatch(
        IReadOnlyDictionary<string, PlcReadSignalUpdate> signalUpdates)
        => PublishReadBatch(signalUpdates);

    internal void PublishReadBatch(IReadOnlyDictionary<string, PlcReadSignalUpdate> signalUpdates)
    {
        ArgumentNullException.ThrowIfNull(signalUpdates);
        if (signalUpdates.Count == 0)
        {
            return;
        }

        var capturedUpdates = CaptureAndValidateReadBatch(signalUpdates);
        string[] affected;
        lock (_readSync)
        {
            var next = (ushort[])Volatile.Read(ref _readBuffer).Clone();
            var nextStates = CopyReadSignalStates();
            var generation = NextReadGeneration();
            affected = capturedUpdates.Select(static pair => pair.Key).ToArray();
            foreach (var (signalKey, update) in capturedUpdates)
            {
                var words = update.ReadSucceeded
                    ? NormalizeWords(update.CurrentWords)
                    : CreateFailedWords(signalKey, update.CurrentWords.Length);
                var normalizedUpdate = update with { CurrentWords = words };
                nextStates[signalKey] = CreateNextReadState(
                    nextStates.GetValueOrDefault(signalKey),
                    generation,
                    normalizedUpdate);
                if (TryGetBinding(_readBindings, signalKey, out var binding))
                {
                    ApplyWords(next, binding, words);
                }
            }

            Interlocked.Exchange(ref _readBuffer, next);
            Volatile.Write(ref _readSignalStates, nextStates);
        }

        foreach (var signalKey in affected)
        {
            NotifyChanged(signalKey, "Read");
        }
    }

    internal bool TryGetReadSignalState(string signalKey, out PlcReadSignalState state)
    {
        var snapshot = Volatile.Read(ref _readSignalStates);
        if (!snapshot.TryGetValue(signalKey, out var current))
        {
            state = default!;
            return false;
        }

        state = current with
        {
            CurrentWords = (ushort[])current.CurrentWords.Clone(),
            LastSucceededWords = (ushort[])current.LastSucceededWords.Clone()
        };
        return true;
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

    private static void ApplyWords(
        ushort[] buffer,
        PlcBufferSignalBinding binding,
        IReadOnlyList<ushort> words)
    {
        for (var index = 0; index < Math.Min(words.Count, binding.AddressCount); index++)
        {
            var bufferIndex = binding.Offset + index;
            if (bufferIndex >= 0 && bufferIndex < buffer.Length)
            {
                buffer[bufferIndex] = words[index];
            }
        }
    }

    private Dictionary<string, PlcReadSignalState> CopyReadSignalStates()
        => Volatile.Read(ref _readSignalStates)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

    private static PlcReadSignalState CreateNextReadState(
        PlcReadSignalState? previous,
        long generation,
        PlcReadSignalUpdate update)
    {
        var currentWords = (ushort[])update.CurrentWords.Clone();
        if (update.ReadSucceeded)
        {
            return new PlcReadSignalState(
                currentWords,
                ReadSucceeded: true,
                generation,
                update.BatchId,
                update.AttemptedAtUtc,
                LastSucceededAtUtc: update.AttemptedAtUtc,
                LastSucceededWords: (ushort[])currentWords.Clone(),
                FailedAtUtc: null,
                FailureReason: null);
        }

        return new PlcReadSignalState(
            currentWords,
            ReadSucceeded: false,
            generation,
            update.BatchId,
            update.AttemptedAtUtc,
            LastSucceededAtUtc: previous?.LastSucceededAtUtc,
            LastSucceededWords: previous is null
                ? []
                : (ushort[])previous.LastSucceededWords.Clone(),
            FailedAtUtc: update.AttemptedAtUtc,
            FailureReason: update.FailureReason);
    }

    private long NextReadGeneration()
    {
        _readGeneration = checked(_readGeneration + 1);
        return _readGeneration;
    }

    private ushort[] CreateFailedWords(string signalKey, int updateWordCount)
    {
        var wordCount = TryGetBinding(_readBindings, signalKey, out var binding)
            ? binding.AddressCount
            : updateWordCount;
        return new ushort[Math.Max(1, wordCount)];
    }

    private static IReadOnlyList<KeyValuePair<string, PlcReadSignalUpdate>> CaptureAndValidateReadBatch(
        IReadOnlyDictionary<string, PlcReadSignalUpdate> signalUpdates)
    {
        var captured = new List<KeyValuePair<string, PlcReadSignalUpdate>>(signalUpdates.Count);
        Guid? batchId = null;
        DateTimeOffset? attemptedAtUtc = null;
        foreach (var (signalKey, update) in signalUpdates)
        {
            if (string.IsNullOrWhiteSpace(signalKey)
                || update is null
                || update.CurrentWords is null
                || update.BatchId == Guid.Empty)
            {
                throw new ArgumentException(
                    "PLC 整批发布包含无效的信号或批次元数据。",
                    nameof(signalUpdates));
            }

            var normalizedAttemptedAtUtc = update.AttemptedAtUtc.ToUniversalTime();
            batchId ??= update.BatchId;
            attemptedAtUtc ??= normalizedAttemptedAtUtc;
            if (update.BatchId != batchId.Value
                || normalizedAttemptedAtUtc != attemptedAtUtc.Value)
            {
                throw new ArgumentException(
                    "PLC 整批发布的全部信号必须具有同一 BatchId 和采集时间。",
                    nameof(signalUpdates));
            }

            captured.Add(
                KeyValuePair.Create(
                    signalKey,
                    update with
                    {
                        CurrentWords = NormalizeWords(update.CurrentWords),
                        AttemptedAtUtc = normalizedAttemptedAtUtc
                    }));
        }

        return captured;
    }

    private static bool TryGetBinding(
        IReadOnlyDictionary<string, PlcBufferSignalBinding> bindings,
        string signalKey,
        out PlcBufferSignalBinding binding)
        => bindings.TryGetValue(signalKey, out binding!);

    private void NotifyChanged(string signalKey, string direction)
        => SignalValuesChanged?.Invoke(this, new PlcSignalBufferChangedEventArgs(signalKey, direction));
}

internal sealed record PlcReadSignalUpdate(
    ushort[] CurrentWords,
    bool ReadSucceeded,
    Guid BatchId,
    DateTimeOffset AttemptedAtUtc,
    string? FailureReason);

internal sealed record PlcReadSignalState(
    ushort[] CurrentWords,
    bool ReadSucceeded,
    long Generation,
    Guid BatchId,
    DateTimeOffset AttemptedAtUtc,
    DateTimeOffset? LastSucceededAtUtc,
    ushort[] LastSucceededWords,
    DateTimeOffset? FailedAtUtc,
    string? FailureReason);
