using System.Text;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Runtime.Signals;

/// <summary>
/// 基于当前 PLC 缓冲区和硬件 IO 映射的强类型逻辑信号访问器。
/// </summary>
/// <typeparam name="TSignalKey">插件声明的 PLC 信号枚举。</typeparam>
public sealed class BufferLogicalSignalAccessor<TSignalKey> : ILogicalSignalAccessor<TSignalKey>
    where TSignalKey : struct, Enum
{
    private readonly IPlcBuffer _buffer;
    private readonly IModulePlcSignalProfile<TSignalKey> _profile;
    private readonly IReadOnlyDictionary<TSignalKey, SignalBinding> _readBindings;
    private readonly IReadOnlyDictionary<TSignalKey, SignalBinding> _writeBindings;

    public BufferLogicalSignalAccessor(
        IPlcBuffer buffer,
        IReadOnlyCollection<ModuleIoSnapshot> bindings,
        IModulePlcSignalProfile<TSignalKey> profile)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _readBindings = BuildBindings(bindings, ModuleSignalDirection.Read, profile);
        _writeBindings = BuildBindings(bindings, ModuleSignalDirection.Write, profile);
    }

    public static BufferLogicalSignalAccessor<TSignalKey> Create(
        IPlcBuffer buffer,
        ProductionContext context,
        IModulePlcSignalProfile<TSignalKey> profile)
    {
        ArgumentNullException.ThrowIfNull(context);
        var bindings = ProductionContextSignalBindings.Get(context);
        return new BufferLogicalSignalAccessor<TSignalKey>(
            buffer,
            bindings.Count > 0 ? bindings : ToFallbackBindings(profile),
            profile);
    }

    public bool CanRead(TSignalKey key)
        => _readBindings.ContainsKey(key);

    public bool CanWrite(TSignalKey key)
        => _writeBindings.ContainsKey(key);

    public bool TryReadUInt16(TSignalKey key, out ushort value)
    {
        if (_readBindings.TryGetValue(key, out var binding))
        {
            EnsureDataType(binding, key, "UInt16", "Int16", "Bool");
            value = ReadWords(binding)[0];
            return true;
        }

        value = default;
        return false;
    }

    public ushort ReadUInt16(TSignalKey key)
    {
        var binding = GetBinding(_readBindings, key, ModuleSignalDirection.Read);
        EnsureDataType(binding, key, "UInt16", "Int16", "Bool");
        return ReadWords(binding)[0];
    }

    public short ReadInt16(TSignalKey key)
    {
        var binding = GetBinding(_readBindings, key, ModuleSignalDirection.Read);
        EnsureDataType(binding, key, "Int16", "UInt16");
        return unchecked((short)ReadWords(binding)[0]);
    }

    public string ReadAscii(TSignalKey key)
    {
        var binding = GetBinding(_readBindings, key, ModuleSignalDirection.Read);
        EnsureDataType(binding, key, "Ascii");
        var words = ReadWords(binding);
        var builder = new StringBuilder(words.Length * 2);

        foreach (var word in words)
        {
            var low = (byte)(word & 0xFF);
            var high = (byte)(word >> 8);

            if (low != 0)
            {
                builder.Append((char)low);
            }

            if (high != 0)
            {
                builder.Append((char)high);
            }
        }

        return builder.ToString().Trim();
    }

    public IReadOnlyList<int> ReadIntArray(TSignalKey key, int count)
    {
        var binding = GetBinding(_readBindings, key, ModuleSignalDirection.Read);
        EnsureDataType(binding, key, "Int16", "UInt16");
        var words = ReadWords(binding);
        var size = Math.Min(count, words.Length);
        var values = new int[size];

        for (var index = 0; index < size; index++)
        {
            values[index] = words[index];
        }

        return values;
    }

    public IReadOnlyList<bool> ReadBoolArray(TSignalKey key, int count)
    {
        var binding = GetBinding(_readBindings, key, ModuleSignalDirection.Read);
        EnsureDataType(binding, key, "Bool");
        var words = ReadWords(binding);
        var size = Math.Min(count, words.Length);
        var values = new bool[size];

        for (var index = 0; index < size; index++)
        {
            values[index] = words[index] != 0;
        }

        return values;
    }

    public IReadOnlyList<double> ReadFloatArray(TSignalKey key, int count)
    {
        var binding = GetBinding(_readBindings, key, ModuleSignalDirection.Read);
        EnsureDataType(binding, key, "Float");
        var words = ReadWords(binding);
        var size = Math.Min(count, words.Length / 2);
        var values = new double[size];

        for (var index = 0; index < size; index++)
        {
            var baseOffset = index * 2;
            values[index] = CombineToFloat(words[baseOffset + 1], words[baseOffset]);
        }

        return values;
    }

    public void WriteUInt16(TSignalKey key, ushort value)
    {
        var binding = GetBinding(_writeBindings, key, ModuleSignalDirection.Write);
        EnsureDataType(binding, key, "UInt16", "Int16", "Bool");
        _buffer.SetWriteValue(binding.SignalKey, 0, value);
        _buffer.SetWriteValue(binding.FallbackOffset, value);
    }

    private ushort[] ReadWords(SignalBinding binding)
    {
        if (_buffer.TryGetReadWords(binding.SignalKey, out var signalWords))
        {
            return NormalizeWords(signalWords, binding.AddressCount);
        }

        var words = new ushort[binding.AddressCount];
        for (var offset = 0; offset < words.Length; offset++)
        {
            words[offset] = _buffer.GetReadValue(binding.FallbackOffset + offset);
        }

        return words;
    }

    private SignalBinding GetBinding(
        IReadOnlyDictionary<TSignalKey, SignalBinding> bindings,
        TSignalKey key,
        ModuleSignalDirection direction)
    {
        if (bindings.TryGetValue(key, out var binding))
        {
            return binding;
        }

        var signal = _profile.Get(key);
        var directionText = direction == ModuleSignalDirection.Write ? "Write" : "Read";
        throw new InvalidOperationException(
            $"模块【{_profile.ModuleId}】信号【{signal.DisplayName}】未绑定 {directionText} IO 映射。");
    }

    private static IReadOnlyDictionary<TSignalKey, SignalBinding> BuildBindings(
        IReadOnlyCollection<ModuleIoSnapshot> bindings,
        ModuleSignalDirection direction,
        IModulePlcSignalProfile<TSignalKey> profile)
    {
        var definitionsBySignalKey = profile.Signals.ToDictionary(
            static signal => NormalizeSignalKey(signal.SignalKey),
            static signal => signal,
            StringComparer.OrdinalIgnoreCase);
        var indexes = new Dictionary<TSignalKey, SignalBinding>();
        var currentOffset = 0;
        var directionText = direction == ModuleSignalDirection.Write ? "Write" : "Read";

        foreach (var binding in bindings
                     .Where(binding => string.Equals(binding.Direction, directionText, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(binding => binding.SortOrder))
        {
            var signalKey = NormalizeSignalKey(binding.SignalKey);
            if (!definitionsBySignalKey.TryGetValue(signalKey, out var definition))
            {
                currentOffset += Math.Max(1, binding.AddressCount);
                continue;
            }

            indexes[definition.Key] = new SignalBinding(
                signalKey,
                currentOffset,
                Math.Max(1, binding.AddressCount),
                binding.DataType);
            currentOffset += Math.Max(1, binding.AddressCount);
        }

        return indexes;
    }

    private void EnsureDataType(SignalBinding binding, TSignalKey key, params string[] allowedTypes)
    {
        if (allowedTypes.Any(type => string.Equals(binding.DataType, type, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var signal = _profile.Get(key);
        throw new InvalidOperationException(
            $"模块【{_profile.ModuleId}】信号【{signal.DisplayName}】数据类型不匹配，当前为【{binding.DataType}】，允许类型为【{string.Join("、", allowedTypes)}】。");
    }

    private static IReadOnlyCollection<ModuleIoSnapshot> ToFallbackBindings(
        IModulePlcSignalProfile<TSignalKey> profile)
        => profile.Signals.Select(static signal => new ModuleIoSnapshot(
                signal.SignalKey,
                signal.DefaultAddress,
                signal.AddressCount,
                signal.DataType,
                signal.DirectionText,
                signal.SortOrder,
                signal.Category,
                signal.BusinessGroup,
                signal.SignalName))
            .ToArray();

    private static string NormalizeSignalKey(string signalKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalKey);
        return signalKey.Trim();
    }

    private static ushort[] NormalizeWords(IReadOnlyList<ushort> words, int count)
    {
        var result = new ushort[Math.Max(1, count)];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = index < words.Count ? words[index] : (ushort)0;
        }

        return result;
    }

    private static float CombineToFloat(ushort high, ushort low)
    {
        byte[] bytes =
        [
            (byte)(high >> 8),
            (byte)high,
            (byte)(low >> 8),
            (byte)low
        ];

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToSingle(bytes, 0);
    }

    private sealed record SignalBinding(
        string SignalKey,
        int FallbackOffset,
        int AddressCount,
        string DataType);
}
