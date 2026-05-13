namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services.Modbus;

public enum ModbusAddressKind
{
    Coil,
    DiscreteInput,
    HoldingRegister,
    InputRegister
}

public sealed record ModbusAddress(byte SlaveId, ModbusAddressKind Kind, ushort Offset);

/// <summary>
/// 解析现场 IO 映射中的 Modbus 地址，支持可选从站前缀，例如 2:HR0。
/// </summary>
public interface IModbusAddressParser
{
    ModbusAddress Parse(string address, byte defaultSlaveId = 1);

    bool TryParse(string? address, byte defaultSlaveId, out ModbusAddress result);
}

/// <summary>
/// 默认 Modbus 地址解析器。只处理地址语法，不直接访问 PLC。
/// </summary>
public sealed class ModbusAddressParser : IModbusAddressParser
{
    public ModbusAddress Parse(string address, byte defaultSlaveId = 1)
    {
        if (TryParse(address, defaultSlaveId, out var result))
        {
            return result;
        }

        throw new FormatException(
            $"Modbus 地址“{address}”格式无效。支持 HR0、IR0、C0、DI0、00001、10001、30001、40001，以及 2:HR0 这类从站前缀。");
    }

    public bool TryParse(string? address, byte defaultSlaveId, out ModbusAddress result)
    {
        result = new ModbusAddress(defaultSlaveId, ModbusAddressKind.HoldingRegister, 0);
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var token = address.Trim();
        var slaveId = defaultSlaveId == 0 ? (byte)1 : defaultSlaveId;
        var separatorIndex = token.IndexOf(':');
        if (separatorIndex > 0)
        {
            if (!byte.TryParse(token[..separatorIndex], out slaveId) || slaveId == 0)
            {
                return false;
            }

            token = token[(separatorIndex + 1)..].Trim();
        }

        if (token.Length == 0)
        {
            return false;
        }

        if (token.All(char.IsDigit))
        {
            return TryParseAreaNumber(token, slaveId, out result);
        }

        if (token.Length > 2
            && char.IsDigit(token[0])
            && char.ToUpperInvariant(token[1]) == 'X'
            && int.TryParse(token[2..], out var areaNumber))
        {
            return TryCreatePrefixedAddress(slaveId, $"{token[0]}X", areaNumber, out result);
        }

        var prefixEnd = 0;
        while (prefixEnd < token.Length && !char.IsDigit(token[prefixEnd]))
        {
            prefixEnd++;
        }

        if (prefixEnd == 0 || prefixEnd == token.Length)
        {
            return false;
        }

        var prefix = NormalizePrefix(token[..prefixEnd]);
        if (!int.TryParse(token[prefixEnd..], out var number))
        {
            return false;
        }

        return TryCreatePrefixedAddress(slaveId, prefix, number, out result);
    }

    private static bool TryParseAreaNumber(string token, byte slaveId, out ModbusAddress result)
    {
        result = new ModbusAddress(slaveId, ModbusAddressKind.HoldingRegister, 0);
        if (!int.TryParse(token, out var number))
        {
            return false;
        }

        if (token.Length == 5 && token.StartsWith('0') && number >= 1)
        {
            return TryCreate(slaveId, ModbusAddressKind.Coil, number - 1, out result);
        }

        if (number is >= 10001 and <= 19999)
        {
            return TryCreate(slaveId, ModbusAddressKind.DiscreteInput, number - 10001, out result);
        }

        if (number is >= 30001 and <= 39999)
        {
            return TryCreate(slaveId, ModbusAddressKind.InputRegister, number - 30001, out result);
        }

        if (number is >= 40001 and <= 49999)
        {
            return TryCreate(slaveId, ModbusAddressKind.HoldingRegister, number - 40001, out result);
        }

        return false;
    }

    private static bool TryCreatePrefixedAddress(
        byte slaveId,
        string prefix,
        int number,
        out ModbusAddress result)
    {
        result = new ModbusAddress(slaveId, ModbusAddressKind.HoldingRegister, 0);
        return prefix switch
        {
            "HR" or "HOLDING" or "HOLDINGREGISTER" or "REGISTER" or "R"
                => TryCreate(slaveId, ModbusAddressKind.HoldingRegister, NormalizeRegisterNumber(number, 40001), out result),
            "IR" or "INPUTREGISTER"
                => TryCreate(slaveId, ModbusAddressKind.InputRegister, NormalizeRegisterNumber(number, 30001), out result),
            "C" or "COIL"
                => TryCreate(slaveId, ModbusAddressKind.Coil, number, out result),
            "DI" or "DISCRETEINPUT" or "INPUT"
                => TryCreate(slaveId, ModbusAddressKind.DiscreteInput, NormalizeRegisterNumber(number, 10001), out result),
            "4X"
                => TryCreate(slaveId, ModbusAddressKind.HoldingRegister, NormalizeOneBasedNumber(number), out result),
            "3X"
                => TryCreate(slaveId, ModbusAddressKind.InputRegister, NormalizeOneBasedNumber(number), out result),
            "0X"
                => TryCreate(slaveId, ModbusAddressKind.Coil, NormalizeOneBasedNumber(number), out result),
            "1X"
                => TryCreate(slaveId, ModbusAddressKind.DiscreteInput, NormalizeOneBasedNumber(number), out result),
            _ => false
        };
    }

    private static int NormalizeRegisterNumber(int number, int areaBase)
        => number >= areaBase ? number - areaBase : number;

    private static int NormalizeOneBasedNumber(int number)
        => number <= 0 ? number : number - 1;

    private static bool TryCreate(
        byte slaveId,
        ModbusAddressKind kind,
        int offset,
        out ModbusAddress result)
    {
        result = new ModbusAddress(slaveId, kind, 0);
        if (slaveId == 0 || offset is < 0 or > ushort.MaxValue)
        {
            return false;
        }

        result = new ModbusAddress(slaveId, kind, (ushort)offset);
        return true;
    }

    private static string NormalizePrefix(string prefix)
        => prefix
            .Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
}
