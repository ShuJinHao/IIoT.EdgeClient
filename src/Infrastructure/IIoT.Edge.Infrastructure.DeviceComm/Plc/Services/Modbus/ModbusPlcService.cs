using System.IO.Ports;
using System.Net.Sockets;
using IIoT.Edge.Module.Contracts.Plc;
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
public sealed class ModbusPlcService : IPlcService
{
    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OperationSettleTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(5);
    private readonly ModbusTransportKind _transportKind;
    private readonly IModbusAddressParser _addressParser;
    private readonly ModbusFactory _factory = new();
    private readonly PlcOperationGate _operationGate = new(
        nameof(ModbusPlcService),
        OperationSettleTimeout,
        DisposeTimeout);
    private ModbusConnection? _connection;
    private PlcEndpoint? _endpoint;

    public ModbusPlcService(
        ModbusTransportKind transportKind,
        IModbusAddressParser addressParser)
    {
        _transportKind = transportKind;
        _addressParser = addressParser;
    }

    public bool IsConnected
        => _operationGate.IsOpen && _connection?.IsConnected == true;

    public void Init(PlcEndpoint endpoint)
    {
        _operationGate.ThrowIfNotOpen(nameof(Init));
        _endpoint = _transportKind switch
        {
            ModbusTransportKind.Tcp => endpoint as TcpPlcEndpoint
                ?? throw new ArgumentException("Modbus TCP 必须使用 TCP 端点。", nameof(endpoint)),
            ModbusTransportKind.Rtu => endpoint as SerialPlcEndpoint
                ?? throw new ArgumentException("Modbus RTU 必须使用串口端点。", nameof(endpoint)),
            _ => throw new NotSupportedException($"不支持的 Modbus 传输方式：{_transportKind}")
        };
    }

    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var context = new ConnectionOperationContext();
        return _operationGate.ExecuteAsync(
            nameof(ConnectAsync),
            token => Task.Run(() => ConnectCoreAsync(context, token), CancellationToken.None),
            GetOperationTimeout(),
            () => AbortConnectionAsync(context.Connection ?? _connection),
            _ => ReleaseConnectionAsync(context.Connection ?? _connection),
            cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var context = new ConnectionOperationContext();
        return _operationGate.ExecuteAsync(
            nameof(DisconnectAsync),
            token => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                context.Connection = _connection;
                DisposeConnection();
            }, CancellationToken.None),
            GetOperationTimeout(),
            () => AbortConnectionAsync(context.Connection ?? _connection),
            _ => ReleaseConnectionAsync(context.Connection ?? _connection),
            cancellationToken);
    }

    public async Task<List<T>> ReadDataAsync<T>(
        string address,
        ushort length,
        CancellationToken cancellationToken = default)
    {
        var modbusAddress = _addressParser.Parse(address, GetDefaultSlaveId());
        var context = new ConnectionOperationContext();

        try
        {
            return await _operationGate.ExecuteAsync(
                    $"Read {address}",
                    _ =>
                    {
                        context.Connection = EnsureConnection();
                        return Task.Run(
                            () => ReadCoreAsync<T>(context.Connection.Master, modbusAddress, length),
                            CancellationToken.None);
                    },
                    GetOperationTimeout(),
                    () => AbortConnectionAsync(context.Connection),
                    _ => ReleaseConnectionAsync(context.Connection),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (PlcOperationGate.ShouldWrapOperationException(ex))
        {
            throw new InvalidOperationException($"Read Modbus address {address} failed.", ex);
        }
    }

    public async Task WriteDataAsync<T>(
        string address,
        List<T> data,
        CancellationToken cancellationToken = default)
    {
        var modbusAddress = _addressParser.Parse(address, GetDefaultSlaveId());
        var context = new ConnectionOperationContext();

        try
        {
            await _operationGate.ExecuteAsync(
                    $"Write {address}",
                    _ =>
                    {
                        context.Connection = EnsureConnection();
                        return Task.Run(
                            () => WriteCoreAsync(context.Connection.Master, modbusAddress, data, address),
                            CancellationToken.None);
                    },
                    GetOperationTimeout(),
                    () => AbortConnectionAsync(context.Connection),
                    _ => ReleaseConnectionAsync(context.Connection),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (
            PlcOperationGate.ShouldWrapOperationException(ex)
            && ex is not NotSupportedException)
        {
            throw new InvalidOperationException($"Write Modbus address {address} failed.", ex);
        }
    }

    public ValueTask DisposeAsync()
        => _operationGate.DisposeAsync(() => Task.Run(DisposeConnection));

    private async Task<bool> ConnectCoreAsync(
        ConnectionOperationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        if (IsConnected && _connection is not null)
        {
            return true;
        }

        context.Connection = _connection;
        DisposeConnection();
        context.Connection = CreateConnectionCandidate();

        try
        {
            switch (_endpoint)
            {
                case TcpPlcEndpoint tcpEndpoint:
                    await context.Connection.TcpClient!
                        .ConnectAsync(tcpEndpoint.Host, tcpEndpoint.Port, cancellationToken)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    context.Connection.AttachMaster(_factory.CreateMaster(context.Connection.TcpClient));
                    break;

                case SerialPlcEndpoint:
                    cancellationToken.ThrowIfCancellationRequested();
                    context.Connection.SerialPort!.Open();
                    cancellationToken.ThrowIfCancellationRequested();
                    context.Connection.AttachMaster(
                        _factory.CreateRtuMaster(new SerialPortAdapter(context.Connection.SerialPort)));
                    break;

                default:
                    throw new InvalidOperationException("Modbus endpoint is not initialized.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            _connection = context.Connection;
            return true;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            ReleaseConnection(context.Connection);
            throw;
        }
    }

    private ModbusConnection CreateConnectionCandidate()
        => _endpoint switch
        {
            TcpPlcEndpoint => new ModbusConnection(new TcpClient(), null),
            SerialPlcEndpoint serialEndpoint => new ModbusConnection(
                null,
                new SerialPort(
                    serialEndpoint.PortName,
                    serialEndpoint.BaudRate,
                    ParseParity(serialEndpoint.Parity),
                    serialEndpoint.DataBits,
                    ParseStopBits(serialEndpoint.StopBits))
                {
                    ReadTimeout = ResolveTimeoutMilliseconds(serialEndpoint.ConnectTimeout),
                    WriteTimeout = ResolveTimeoutMilliseconds(serialEndpoint.ConnectTimeout)
                }),
            _ => throw new InvalidOperationException("Modbus endpoint is not initialized.")
        };

    private static async Task<List<T>> ReadCoreAsync<T>(
        IModbusMaster master,
        ModbusAddress address,
        ushort length)
        => address.Kind switch
        {
            ModbusAddressKind.Coil => ConvertFromBits<T>(
                await master.ReadCoilsAsync(address.SlaveId, address.Offset, length).ConfigureAwait(false)),
            ModbusAddressKind.DiscreteInput => ConvertFromBits<T>(
                await master.ReadInputsAsync(address.SlaveId, address.Offset, length).ConfigureAwait(false)),
            ModbusAddressKind.InputRegister => ConvertFromRegisters<T>(
                await master
                    .ReadInputRegistersAsync(address.SlaveId, address.Offset, GetRegisterWordCount<T>(length))
                    .ConfigureAwait(false),
                length),
            _ => ConvertFromRegisters<T>(
                await master
                    .ReadHoldingRegistersAsync(address.SlaveId, address.Offset, GetRegisterWordCount<T>(length))
                    .ConfigureAwait(false),
                length)
        };

    private static Task WriteCoreAsync<T>(
        IModbusMaster master,
        ModbusAddress modbusAddress,
        IReadOnlyCollection<T> data,
        string displayAddress)
        => modbusAddress.Kind switch
        {
            ModbusAddressKind.Coil => WriteCoilsAsync(master, modbusAddress, data),
            ModbusAddressKind.HoldingRegister => WriteRegistersAsync(master, modbusAddress, data),
            _ => throw new NotSupportedException($"Modbus 地址 {displayAddress} 是只读区，不能写入。")
        };

    private static async Task WriteCoilsAsync<T>(IModbusMaster master, ModbusAddress address, IReadOnlyCollection<T> data)
    {
        var values = data.Select(ToBoolean).ToArray();
        if (values.Length == 1)
        {
            await master
                .WriteSingleCoilAsync(address.SlaveId, address.Offset, values[0])
                .ConfigureAwait(false);
            return;
        }

        await master
            .WriteMultipleCoilsAsync(address.SlaveId, address.Offset, values)
            .ConfigureAwait(false);
    }

    private static async Task WriteRegistersAsync<T>(IModbusMaster master, ModbusAddress address, IReadOnlyCollection<T> data)
    {
        var words = ConvertToRegisters(data).ToArray();
        if (words.Length == 1)
        {
            await master
                .WriteSingleRegisterAsync(address.SlaveId, address.Offset, words[0])
                .ConfigureAwait(false);
            return;
        }

        await master
            .WriteMultipleRegistersAsync(address.SlaveId, address.Offset, words)
            .ConfigureAwait(false);
    }

    private byte GetDefaultSlaveId()
        => _endpoint is SerialPlcEndpoint serialEndpoint && serialEndpoint.SlaveId != 0
            ? serialEndpoint.SlaveId
            : (byte)1;

    private TimeSpan GetOperationTimeout()
        => _endpoint?.ConnectTimeout > TimeSpan.Zero
            ? _endpoint.ConnectTimeout
            : DefaultOperationTimeout;

    private void EnsureInitialized()
    {
        if (_endpoint is null)
        {
            throw new InvalidOperationException("Modbus PLC endpoint is not initialized.");
        }
    }

    private ModbusConnection EnsureConnection()
    {
        if (!IsConnected || _connection is null)
        {
            throw new InvalidOperationException("PLC is not connected.");
        }

        return _connection;
    }

    private Task AbortConnectionAsync(ModbusConnection? connection)
        => connection is null
            ? Task.CompletedTask
            : Task.Run(connection.AbortTransport);

    private Task ReleaseConnectionAsync(ModbusConnection? connection)
        => connection is null
            ? Task.CompletedTask
            : Task.Run(() => ReleaseConnection(connection));

    private void ReleaseConnection(ModbusConnection? connection)
    {
        if (connection is null)
        {
            return;
        }

        if (ReferenceEquals(_connection, connection))
        {
            _connection = null;
        }

        connection.Dispose();
    }

    private void DisposeConnection()
    {
        var connection = _connection;
        _connection = null;
        connection?.Dispose();
    }

    private static int ResolveTimeoutMilliseconds(TimeSpan timeout)
        => timeout <= TimeSpan.Zero
            ? (int)DefaultOperationTimeout.TotalMilliseconds
            : (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);

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

    private sealed class ConnectionOperationContext
    {
        public ModbusConnection? Connection { get; set; }
    }

    private sealed class ModbusConnection : IDisposable
    {
        private PlcTransportOwner<IModbusMaster>? _masterOwner;
        private readonly PlcTransportOwner<TcpClient>? _tcpClientOwner;
        private readonly PlcTransportOwner<SerialPort>? _serialPortOwner;
        private int _released;

        public ModbusConnection(TcpClient? tcpClient, SerialPort? serialPort)
        {
            _tcpClientOwner = tcpClient is null
                ? null
                : new PlcTransportOwner<TcpClient>(tcpClient, static value => value.Dispose());
            _serialPortOwner = serialPort is null
                ? null
                : new PlcTransportOwner<SerialPort>(serialPort, static value => value.Dispose());
        }

        public bool IsConnected
            => _tcpClientOwner?.ValueOrDefault?.Connected == true
               || _serialPortOwner?.ValueOrDefault?.IsOpen == true;

        public IModbusMaster Master
            => Volatile.Read(ref _masterOwner)?.Value
               ?? throw new ObjectDisposedException(nameof(IModbusMaster));

        public TcpClient? TcpClient => _tcpClientOwner?.ValueOrDefault;

        public SerialPort? SerialPort => _serialPortOwner?.ValueOrDefault;

        public void AttachMaster(IModbusMaster master)
        {
            ArgumentNullException.ThrowIfNull(master);
            var owner = new PlcTransportOwner<IModbusMaster>(
                master,
                static value => (value as IDisposable)?.Dispose());
            if (Volatile.Read(ref _released) != 0
                || Interlocked.CompareExchange(ref _masterOwner, owner, null) is not null)
            {
                owner.Release();
                throw new InvalidOperationException("Modbus master ownership is no longer available.");
            }
        }

        public void AbortTransport()
        {
            var errors = new List<Exception>();
            ReleaseOwner(_tcpClientOwner, errors);
            ReleaseOwner(_serialPortOwner, errors);
            ThrowIfReleaseFailed(errors);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            var errors = new List<Exception>();
            ReleaseOwner(Interlocked.Exchange(ref _masterOwner, null), errors);
            ReleaseOwner(_tcpClientOwner, errors);
            ReleaseOwner(_serialPortOwner, errors);
            ThrowIfReleaseFailed(errors);
        }

        private static void ReleaseOwner<T>(
            PlcTransportOwner<T>? owner,
            ICollection<Exception> errors)
            where T : class
        {
            if (owner is null)
            {
                return;
            }

            try
            {
                owner.Release();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        private static void ThrowIfReleaseFailed(IReadOnlyCollection<Exception> errors)
        {
            if (errors.Count != 0)
            {
                throw new AggregateException("Modbus connection release failed.", errors);
            }
        }
    }
}
