using System.Text;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Resources;
using IIoT.Edge.Runtime.Signals;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Module.Homogenization.Runtime;

/// <summary>
/// 按宿主注入的 IO 绑定从 PLC 缓冲区读写匀浆信号。
/// </summary>
internal sealed class HomogenizationSignalCodec
{
    private readonly IPlcBuffer _buffer;
    private readonly IReadOnlyDictionary<string, SignalBinding> _readBindings;
    private readonly IReadOnlyDictionary<string, SignalBinding> _writeBindings;

    public HomogenizationSignalCodec(IPlcBuffer buffer, ProductionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

        var bindings = ProductionContextSignalBindings.Get(context);
        if (bindings.Count == 0)
        {
            throw new InvalidOperationException("匀浆运行时需要先由宿主注入 IO 绑定。");
        }

        // 运行时只按数据库绑定计算缓冲区偏移，不按 PLC 地址或模板顺序硬编码取数。
        _readBindings = BuildBindings(bindings, "Read");
        _writeBindings = BuildBindings(bindings, "Write");
    }

    public ushort ReadWord(string label)
        => _buffer.GetReadValue(GetReadBinding(label).Offset);

    public short ReadInt16(string label)
        => unchecked((short)ReadWord(label));

    public void WriteWord(string label, ushort value)
        => _buffer.SetWriteValue(GetWriteBinding(label).Offset, value);

    /// <summary>
    /// 读取按低字节在前、高字节在后的 ASCII 托盘码。
    /// </summary>
    public string ReadAsciiString(string label)
    {
        var binding = GetReadBinding(label);
        var builder = new StringBuilder(binding.AddressCount * 2);

        for (var offset = 0; offset < binding.AddressCount; offset++)
        {
            var word = _buffer.GetReadValue(binding.Offset + offset);
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

    public IReadOnlyList<int> ReadIntList(string label, int count)
    {
        var binding = GetReadBinding(label);
        var size = Math.Min(count, binding.AddressCount);
        var values = new int[size];

        for (var index = 0; index < size; index++)
        {
            values[index] = _buffer.GetReadValue(binding.Offset + index);
        }

        return values;
    }

    public IReadOnlyList<bool> ReadBoolList(string label, int count)
    {
        var binding = GetReadBinding(label);
        var size = Math.Min(count, binding.AddressCount);
        var values = new bool[size];

        for (var index = 0; index < size; index++)
        {
            values[index] = _buffer.GetReadValue(binding.Offset + index) != 0;
        }

        return values;
    }

    public IReadOnlyList<double> ReadFloatList(string label, int count)
    {
        var binding = GetReadBinding(label);
        var size = Math.Min(count, binding.AddressCount / 2);
        var values = new double[size];

        for (var index = 0; index < size; index++)
        {
            var baseOffset = binding.Offset + (index * 2);
            values[index] = CombineToFloat(
                _buffer.GetReadValue(baseOffset + 1),
                _buffer.GetReadValue(baseOffset));
        }

        return values;
    }

    /// <summary>
    /// 从实时数据 label 组采集当前 PLC 快照。
    /// </summary>
    public HomogenizationRealtimeSnapshot CaptureRealtimeSnapshot()
        => new()
        {
            CapturedAt = DateTime.UtcNow,
            StirringSpeed = ReadInt16(HomogenizationPlcSignalProfile.RealtimeStirringSpeed.Label),
            StirringCurrent = ReadInt16(HomogenizationPlcSignalProfile.RealtimeStirringCurrent.Label),
            DispersionSpeed = ReadInt16(HomogenizationPlcSignalProfile.RealtimeDispersionSpeed.Label),
            DispersionCurrent = ReadInt16(HomogenizationPlcSignalProfile.RealtimeDispersionCurrent.Label),
            Temperature = ReadInt16(HomogenizationPlcSignalProfile.RealtimeTemperature.Label),
            Vacuum = ReadInt16(HomogenizationPlcSignalProfile.RealtimeVacuum.Label)
        };

    /// <summary>
    /// 从配方 label 组采集数组参数，浮点值按两个 PLC word 合成。
    /// </summary>
    public HomogenizationRecipeSnapshot CaptureRecipeSnapshot()
        => new()
        {
            CapturedAt = DateTime.UtcNow,
            StirringSpeed = ReadIntList(HomogenizationPlcSignalProfile.RecipeStirringSpeed.Label, 30),
            DispersionSpeed = ReadIntList(HomogenizationPlcSignalProfile.RecipeDispersionSpeed.Label, 30),
            Ncm = ReadFloatList(HomogenizationPlcSignalProfile.RecipeNcm.Label, 30),
            Sp1 = ReadFloatList(HomogenizationPlcSignalProfile.RecipeSp1.Label, 30),
            Nmp = ReadFloatList(HomogenizationPlcSignalProfile.RecipeNmp.Label, 30),
            GlueSolution = ReadFloatList(HomogenizationPlcSignalProfile.RecipeGlueSolution.Label, 30),
            Cnt = ReadFloatList(HomogenizationPlcSignalProfile.RecipeCnt.Label, 30),
            Vacuum = ReadBoolList(HomogenizationPlcSignalProfile.RecipeVacuum.Label, 30),
            Time = ReadIntList(HomogenizationPlcSignalProfile.RecipeTime.Label, 30),
            Temperature = ReadIntList(HomogenizationPlcSignalProfile.RecipeTemperature.Label, 30)
                .Select(static value => (double)value)
                .ToArray(),
            StopStep = ReadBoolList(HomogenizationPlcSignalProfile.RecipeStopStep.Label, 30)
        };

    /// <summary>
    /// 读取设备状态码，并按 MES 码表转换为状态文本。
    /// </summary>
    public HomogenizationEquipmentStatusSnapshot CaptureEquipmentStatusSnapshot(HomogenizationMesCodeOptions mesCodes)
    {
        var statusCode = ReadInt16(HomogenizationPlcSignalProfile.EquipmentStatusValue.Label);
        var statusText = mesCodes.ResolveEquipmentStatusText(statusCode);

        var messages = new List<string>();
        if (statusCode == -1)
        {
            messages.Add("PLC 返回报警状态。");
        }

        var unknownStatus = HomogenizationText.Get("Homogenization_EquipmentStatus_Unknown", "未知");
        if (string.Equals(statusText, unknownStatus, StringComparison.Ordinal))
        {
            messages.Add($"设备状态码未知：{statusCode}。");
        }

        return new HomogenizationEquipmentStatusSnapshot
        {
            CapturedAt = DateTime.UtcNow,
            StatusCode = statusCode,
            StatusText = statusText,
            Messages = messages
        };
    }

    private SignalBinding GetReadBinding(string label)
        => GetBinding(_readBindings, label, "Read");

    private SignalBinding GetWriteBinding(string label)
        => GetBinding(_writeBindings, label, "Write");

    private static SignalBinding GetBinding(
        IReadOnlyDictionary<string, SignalBinding> bindings,
        string label,
        string direction)
    {
        var normalized = NormalizeLabel(label);
        if (!bindings.TryGetValue(normalized, out var binding))
        {
            throw new InvalidOperationException($"匀浆运行时未绑定 {direction} 信号“{label}”。");
        }

        return binding;
    }

    private static IReadOnlyDictionary<string, SignalBinding> BuildBindings(
        IReadOnlyCollection<ModuleIoSnapshot> bindings,
        string direction)
    {
        var indexes = new Dictionary<string, SignalBinding>(StringComparer.OrdinalIgnoreCase);
        var currentOffset = 0;

        foreach (var binding in bindings
                     .Where(binding => string.Equals(binding.Direction, direction, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(binding => binding.SortOrder))
        {
            indexes[NormalizeLabel(binding.Label)] = new SignalBinding(currentOffset, Math.Max(1, binding.AddressCount));
            currentOffset += Math.Max(1, binding.AddressCount);
        }

        return indexes;
    }

    private static string NormalizeLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return label.Trim();
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

    private sealed record SignalBinding(int Offset, int AddressCount);
}
