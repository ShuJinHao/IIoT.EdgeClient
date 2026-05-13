using System.IO.Ports;
using System.Net.Sockets;
using IIoT.Edge.Application.Abstractions.Plc;
using NModbus;
using NModbus.Serial;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services.Modbus;

public enum ModbusTransportKind
{
    Tcp,
    Rtu
}

/// <summary>
/// 基于 NModbus 的 Modbus TCP/RTU PLC 通信服务，只做端点适配和数据类型转换，不手写 Modbus 协议栈。
/// </summary>
public sealed class ModbusPlcService : IPlcService, IDisposable
{
    private readonly ModbusTransportKind _transportKind;
    private readonly IModbusAddressParser _addressParser;
    private readonly ModbusFactory _factory = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IModbusMaster? _master;
    private TcpClient? _tcpClient;
    private SerialPort? _serialPort;
    private PlcEndpoint? _endpoint;

    public ModbusPlcService(
        ModbusTransportKind transportKind,
        IModbusAddressParser addressParser)
    {
        _transportKind = transportKind;
        _addressParser = addressParser;
    }

    public bool IsConnected
        => _transportKind == ModbusTransportKind.Tcp
            ? _tcpClient?.Connected == true
            : _serialPort?.IsOpen == true;

    public void Init(PlcEndpoint endpoint)
    {
        _endpoint = _transportKind switch
        {
            ModbusTransportKind.Tcp => endpoint as TcpPlcEndpoint
                ?? throw new ArgumentException("Modbus TCP 必须使用 TCP 端点。", nameof(endpoint)),
            ModbusTransportKind.Rtu => endpoint as SerialPlcEndpoint
                ?? throw new ArgumentException("Modbus RTU 必须使用串口端点。", nameof(endpoint)),
            _ => throw new NotSupportedException($"不支持的 Modbus 传输方式：{_transportKind}")
        };
    }

    public async Task<bool> ConnectAsync()
    {
        EnsureInitialized();
        if (IsConnected && _master is not null)
        {
            return true;
        }

        DisposeConnection();
        if (_endpoint is TcpPlcEndpoint tcpEndpoint)
        {
            await ConnectTcpAsync(tcpEndpoint).ConfigureAwait(false);
            return true;
        }

        if (_endpoint is SerialPlcEndpoint serialEndpoint)
        {
            ConnectRtu(serialEndpoint);
            return true;
        }

        throw new InvalidOperationException("Modbus endpoint is not initialized.");
    }

    public void Disconnect()
        => DisposeConnection();

    public async Task<List<T>> ReadDataAsync<T>(string address, ushort length)
    {
        var master = EnsureMaster();
        var modbusAddress = _addressParser.Parse(address, GetDefaultSlaveId());

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return modbusAddress.Kind switch
            {
                ModbusAddressKind.Coil => ConvertFromBits<T>(
                    await master
                        .ReadCoilsAsync(modbusAddress.SlaveId, modbusAddress.Offset, length)
                        .WaitAsync(GetOperationTimeout())
                        .ConfigureAwait(false)),
                ModbusAddressKind.DiscreteInput => ConvertFromBits<T>(
                    await master
                        .ReadInputsAsync(modbusAddress.SlaveId, modbusAddress.Offset, length)
                        .WaitAsync(GetOperationTimeout())
                        .ConfigureAwait(false)),
                ModbusAddressKind.InputRegister => ConvertFromRegisters<T>(
                    await master
                        .ReadInputRegistersAsync(modbusAddress.SlaveId, modbusAddress.Offset, GetRegisterWordCount<T>(length))
                        .WaitAsync(GetOperationTimeout())
                        .ConfigureAwait(false),
                    length),
                _ => ConvertFromRegisters<T>(
                    await master
                        .ReadHoldingRegistersAsync(modbusAddress.SlaveId, modbusAddress.Offset, GetRegisterWordCount<T>(length))
                        .WaitAsync(GetOperationTimeout())
                        .ConfigureAwait(false),
                    length)
            };
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            throw new InvalidOperationException($"Read Modbus address {address} failed.", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task WriteDataAsync<T>(string address, List<T> data)
    {
        var master = EnsureMaster();
        var modbusAddress = _addressParser.Parse(address, GetDefaultSlaveId());

        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            switch (modbusAddress.Kind)
            {
                case ModbusAddressKind.Coil:
                    await WriteCoilsAsync(master, modbusAddress, data).ConfigureAwait(false);
                    return;

                case ModbusAddressKind.HoldingRegister:
                    await WriteRegistersAsync(master, modbusAddress, data).ConfigureAwait(false);
                    return;

                default:
                    throw new NotSupportedException($"Modbus 地址 {address} 是只读区，不能写入。");
            }
        }
        catch (Exception ex) when (ex is not TimeoutException and not NotSupportedException)
        {
            throw new InvalidOperationException($"Write Modbus address {address} failed.", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        DisposeConnection();
        _semaphore.Dispose();
    }

    private async Task ConnectTcpAsync(TcpPlcEndpoint endpoint)
    {
        var client = new TcpClient();
        try
        {
            await client
                .ConnectAsync(endpoint.Host, endpoint.Port)
                .WaitAsync(endpoint.ConnectTimeout)
                .ConfigureAwait(false);
            _tcpClient = client;
            _master = _factory.CreateMaster(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private void ConnectRtu(SerialPlcEndpoint endpoint)
    {
        var serialPort = new SerialPort(
            endpoint.PortName,
            endpoint.BaudRate,
            ParseParity(endpoint.Parity),
            endpoint.DataBits,
            ParseStopBits(endpoint.StopBits))
        {
            ReadTimeout = (int)endpoint.ConnectTimeout.TotalMilliseconds,
            WriteTimeout = (int)endpoint.ConnectTimeout.TotalMilliseconds
        };

        try
        {
            serialPort.Open();
            _serialPort = serialPort;
            _master = _factory.CreateRtuMaster(new SerialPortAdapter(serialPort));
        }
        catch
        {
            serialPort.Dispose();
            throw;
        }
    }

    private async Task WriteCoilsAsync<T>(IModbusMaster master, ModbusAddress address, IReadOnlyCollection<T> data)
    {
        var values = data.Select(ToBoolean).ToArray();
        if (values.Length == 1)
        {
            await master
                .WriteSingleCoilAsync(address.SlaveId, address.Offset, values[0])
                .WaitAsync(GetOperationTimeout())
                .ConfigureAwait(false);
            return;
        }

        await master
            .WriteMultipleCoilsAsync(address.SlaveId, address.Offset, values)
            .WaitAsync(GetOperationTimeout())
            .ConfigureAwait(false);
    }

    private async Task WriteRegistersAsync<T>(IModbusMaster master, ModbusAddress address, IReadOnlyCollection<T> data)
    {
        var words = ConvertToRegisters(data).ToArray();
        if (words.Length == 1)
        {
            await master
                .WriteSingleRegisterAsync(address.SlaveId, address.Offset, words[0])
                .WaitAsync(GetOperationTimeout())
                .ConfigureAwait(false);
            return;
        }

        await master
            .WriteMultipleRegistersAsync(address.SlaveId, address.Offset, words)
            .WaitAsync(GetOperationTimeout())
            .ConfigureAwait(false);
    }

    private byte GetDefaultSlaveId()
        => _endpoint is SerialPlcEndpoint serialEndpoint && serialEndpoint.SlaveId != 0
            ? serialEndpoint.SlaveId
            : (byte)1;

    private TimeSpan GetOperationTimeout()
        => _endpoint?.ConnectTimeout ?? TimeSpan.FromSeconds(3);

    private void EnsureInitialized()
    {
        if (_endpoint is null)
        {
            throw new InvalidOperationException("Modbus PLC endpoint is not initialized.");
        }
    }

    private IModbusMaster EnsureMaster()
    {
        if (_master is null || !IsConnected)
        {
            throw new InvalidOperationException("PLC is not connected.");
        }

        return _master;
    }

    private void DisposeConnection()
    {
        try
        {
            (_master as IDisposable)?.Dispose();
        }
        catch
        {
        }

        _master = null;
        _tcpClient?.Dispose();
        _tcpClient = null;

        if (_serialPort is not null)
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
            }

            _serialPort.Dispose();
            _serialPort = null;
        }
    }

    private static ushort GetRegisterWordCount<T>(ushort elementCount)
        => checked((ushort)(elementCount * GetWordSize(typeof(T))));

    private static int GetWordSize(Type type)
    {
        if (type == typeof(bool)
            || type == typeof(short)
            || type == typeof(ushort))
        {
            return 1;
        }

        if (type == typeof(int)
            || type == typeof(uint)
            || type == typeof(float))
        {
            return 2;
        }

        throw new NotSupportedException($"Modbus 不支持读取 {type.Name} 类型。");
    }

    private static List<T> ConvertFromBits<T>(IReadOnlyList<bool> bits)
        => bits.Select(ConvertBit<T>).ToList();

    private static T ConvertBit<T>(bool value)
    {
        if (typeof(T) == typeof(bool))
        {
            return (T)(object)value;
        }

        if (typeof(T) == typeof(ushort))
        {
            return (T)(object)(ushort)(value ? 1 : 0);
        }

        if (typeof(T) == typeof(short))
        {
            return (T)(object)(short)(value ? 1 : 0);
        }

        if (typeof(T) == typeof(int))
        {
            return (T)(object)(value ? 1 : 0);
        }

        throw new NotSupportedException($"Modbus 位地址不支持读取 {typeof(T).Name} 类型。");
    }

    private static List<T> ConvertFromRegisters<T>(IReadOnlyList<ushort> words, ushort elementCount)
    {
        var result = new List<T>(elementCount);
        var wordSize = GetWordSize(typeof(T));
        for (var index = 0; index < elementCount; index++)
        {
            var wordIndex = index * wordSize;
            if (wordIndex + wordSize > words.Count)
            {
                throw new InvalidOperationException($"Modbus 返回 {words.Count} 个 word，少于请求的 {elementCount} 个元素。");
            }

            object value = Type.GetTypeCode(typeof(T)) switch
            {
                TypeCode.Boolean => words[wordIndex] != 0,
                TypeCode.Int16 => unchecked((short)words[wordIndex]),
                TypeCode.UInt16 => words[wordIndex],
                TypeCode.Int32 => CombineToInt32(words[wordIndex], words[wordIndex + 1]),
                TypeCode.UInt32 => CombineToUInt32(words[wordIndex], words[wordIndex + 1]),
                TypeCode.Single => CombineToFloat(words[wordIndex], words[wordIndex + 1]),
                _ => throw new NotSupportedException($"Modbus 不支持读取 {typeof(T).Name} 类型。")
            };
            result.Add((T)value);
        }

        return result;
    }

    private static IEnumerable<ushort> ConvertToRegisters<T>(IEnumerable<T> data)
    {
        foreach (var item in data)
        {
            switch (Type.GetTypeCode(typeof(T)))
            {
                case TypeCode.Boolean:
                    yield return ToBoolean(item) ? (ushort)1 : (ushort)0;
                    break;
                case TypeCode.Int16:
                case TypeCode.UInt16:
                    yield return Convert.ToUInt16(item);
                    break;
                case TypeCode.Int32:
                    foreach (var word in SplitInt32(Convert.ToInt32(item)))
                    {
                        yield return word;
                    }
                    break;
                case TypeCode.UInt32:
                    foreach (var word in SplitUInt32(Convert.ToUInt32(item)))
                    {
                        yield return word;
                    }
                    break;
                case TypeCode.Single:
                    foreach (var word in SplitFloat(Convert.ToSingle(item)))
                    {
                        yield return word;
                    }
                    break;
                default:
                    throw new NotSupportedException($"Modbus 不支持写入 {typeof(T).Name} 类型。");
            }
        }
    }

    private static bool ToBoolean<T>(T value)
        => value switch
        {
            bool typed => typed,
            ushort typed => typed != 0,
            short typed => typed != 0,
            int typed => typed != 0,
            uint typed => typed != 0,
            _ => throw new NotSupportedException($"Modbus 位地址不支持写入 {typeof(T).Name} 类型。")
        };

    private static int CombineToInt32(ushort high, ushort low)
        => (high << 16) | low;

    private static uint CombineToUInt32(ushort high, ushort low)
        => ((uint)high << 16) | low;

    private static float CombineToFloat(ushort high, ushort low)
    {
        var bytes = new[]
        {
            (byte)(high >> 8),
            (byte)high,
            (byte)(low >> 8),
            (byte)low
        };

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToSingle(bytes, 0);
    }

    private static ushort[] SplitInt32(int value)
        => [(ushort)((value >> 16) & 0xFFFF), (ushort)(value & 0xFFFF)];

    private static ushort[] SplitUInt32(uint value)
        => [(ushort)((value >> 16) & 0xFFFF), (ushort)(value & 0xFFFF)];

    private static ushort[] SplitFloat(float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return
        [
            (ushort)((bytes[0] << 8) | bytes[1]),
            (ushort)((bytes[2] << 8) | bytes[3])
        ];
    }

    private static StopBits ParseStopBits(string value)
        => Enum.TryParse<StopBits>(value, ignoreCase: true, out var result)
            ? result
            : throw new ArgumentException($"停止位配置无效：{value}");

    private static Parity ParseParity(string value)
        => Enum.TryParse<Parity>(value, ignoreCase: true, out var result)
            ? result
            : throw new ArgumentException($"校验位配置无效：{value}");
}
