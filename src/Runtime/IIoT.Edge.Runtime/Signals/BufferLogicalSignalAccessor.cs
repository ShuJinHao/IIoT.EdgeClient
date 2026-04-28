using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Runtime.Signals;

public sealed class BufferLogicalSignalAccessor : ILogicalSignalAccessor
{
    private readonly IPlcBuffer _buffer;
    private readonly IReadOnlyDictionary<string, int> _readIndexes;
    private readonly IReadOnlyDictionary<string, int> _writeIndexes;

    public BufferLogicalSignalAccessor(
        IPlcBuffer buffer,
        IReadOnlyCollection<ModuleIoSnapshot> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _readIndexes = BuildIndexes(bindings, "Read");
        _writeIndexes = BuildIndexes(bindings, "Write");
    }

    public BufferLogicalSignalAccessor(
        IPlcBuffer buffer,
        IReadOnlyCollection<ModuleSignalDefinition> signalDefinitions)
    {
        ArgumentNullException.ThrowIfNull(signalDefinitions);
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _readIndexes = BuildIndexes(signalDefinitions, ModuleSignalDirection.Read);
        _writeIndexes = BuildIndexes(signalDefinitions, ModuleSignalDirection.Write);
    }

    public static BufferLogicalSignalAccessor Create(
        IPlcBuffer buffer,
        ProductionContext context,
        IReadOnlyCollection<ModuleSignalDefinition> fallbackDefinitions)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bindings = ProductionContextSignalBindings.Get(context);
        return bindings.Count > 0
            ? new BufferLogicalSignalAccessor(buffer, bindings)
            : new BufferLogicalSignalAccessor(buffer, fallbackDefinitions);
    }

    public bool CanRead(string label)
        => _readIndexes.ContainsKey(NormalizeLabel(label));

    public bool CanWrite(string label)
        => _writeIndexes.ContainsKey(NormalizeLabel(label));

    public bool TryRead(string label, out ushort value)
    {
        var key = NormalizeLabel(label);
        if (_readIndexes.TryGetValue(key, out var index))
        {
            value = _buffer.GetReadValue(index);
            return true;
        }

        value = default;
        return false;
    }

    public ushort Read(string label)
    {
        var key = NormalizeLabel(label);
        if (!_readIndexes.TryGetValue(key, out var index))
        {
            throw new InvalidOperationException($"Read signal '{label}' is not bound.");
        }

        return _buffer.GetReadValue(index);
    }

    public void Write(string label, ushort value)
    {
        var key = NormalizeLabel(label);
        if (!_writeIndexes.TryGetValue(key, out var index))
        {
            throw new InvalidOperationException($"Write signal '{label}' is not bound.");
        }

        _buffer.SetWriteValue(index, value);
    }

    private static IReadOnlyDictionary<string, int> BuildIndexes(
        IReadOnlyCollection<ModuleIoSnapshot> bindings,
        string direction)
    {
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var currentIndex = 0;

        foreach (var binding in bindings
                     .Where(binding => string.Equals(binding.Direction, direction, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(binding => binding.SortOrder))
        {
            indexes.Add(NormalizeLabel(binding.Label), currentIndex);
            currentIndex += Math.Max(1, binding.AddressCount);
        }

        return indexes;
    }

    private static IReadOnlyDictionary<string, int> BuildIndexes(
        IReadOnlyCollection<ModuleSignalDefinition> signalDefinitions,
        ModuleSignalDirection direction)
    {
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var currentIndex = 0;

        foreach (var definition in signalDefinitions
                     .Where(definition => definition.Direction == direction)
                     .OrderBy(definition => definition.SortOrder))
        {
            indexes.Add(NormalizeLabel(definition.Label), currentIndex);
            currentIndex += Math.Max(1, definition.AddressCount);
        }

        return indexes;
    }

    private static string NormalizeLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return label.Trim();
    }
}
