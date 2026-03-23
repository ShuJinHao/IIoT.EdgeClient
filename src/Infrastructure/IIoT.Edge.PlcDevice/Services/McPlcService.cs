using IIoT.Edge.Contracts.Plc;
using MCProtocol;
using static MCProtocol.Mitsubishi;

namespace IIoT.Edge.PlcDevice.Services;

public class McPlcService : IPlcService, IDisposable
{
    private McProtocolTcp _mcProtocol = null!;
    private string _ip = string.Empty;
    private int _port;
    private readonly McFrame _frameType = McFrame.MC3E;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public bool IsConnected => _mcProtocol?.Connected ?? false;

    public void Init(string ip, int port)
    {
        _ip = ip;
        _port = port;
    }

    public bool Connect()
    {
        try
        {
            if (_mcProtocol?.Connected ?? false)
                return true;

            _mcProtocol = new McProtocolTcp(_ip, _port, _frameType);
            _mcProtocol.Open().GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"连接失败: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ConnectAsync()
    {
        if (_mcProtocol?.Connected ?? false)
            return true;

        _mcProtocol?.Close();
        _mcProtocol = new McProtocolTcp(_ip, _port, _frameType);

        try
        {
            var connectTask = _mcProtocol.Open();
            if (await Task.WhenAny(connectTask, Task.Delay(3000)) != connectTask)
            {
                Console.WriteLine("连接超时");
                return false;
            }

            await connectTask;
            return _mcProtocol.Connected;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"连接失败: {ex.Message}");
            return false;
        }
    }

    public void Disconnect() => _mcProtocol?.Close();

    public List<T> ReadData<T>(string address, ushort length)
        => ReadDataAsync<T>(address, length).GetAwaiter().GetResult();

    public async Task<List<T>> ReadDataAsync<T>(string address, ushort length)
    {
        if (!IsConnected)
            throw new InvalidOperationException("PLC未连接");

        await _semaphore.WaitAsync();
        try
        {
            if (typeof(T) == typeof(bool))
            {
                var data = new int[length];
                await _mcProtocol.GetBitDevice(address, length, data);
                return data.Select(x => (T)(object)(x != 0)).ToList();
            }
            else
            {
                int wordSize = GetWordSize(typeof(T));
                int totalWords = length * wordSize;
                var data = new int[totalWords];
                await _mcProtocol.ReadDeviceBlock(address, (ushort)totalWords, data);
                return ConvertToTypeList<T>(data, length);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"读取地址 {address} 失败: {ex.Message}", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void WriteData<T>(string address, List<T> data)
        => WriteDataAsync(address, data).GetAwaiter().GetResult();

    public async Task WriteDataAsync<T>(string address, List<T> data)
    {
        if (!IsConnected)
            throw new InvalidOperationException("PLC未连接");

        await _semaphore.WaitAsync();
        try
        {
            if (typeof(T) == typeof(bool))
            {
                var intData = data.Select(x => Convert.ToInt32(x)).ToArray();
                await _mcProtocol.SetBitDevice(address, data.Count, intData);
            }
            else
            {
                int wordSize = GetWordSize(typeof(T));
                int[] intData = ConvertToIntArray(data, wordSize);
                await _mcProtocol.WriteDeviceBlock(address, (ushort)intData.Length, intData);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"写入地址 {address} 失败: {ex.Message}", ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        Disconnect();
        _mcProtocol?.Dispose();
    }

    private static int GetWordSize(Type type)
    {
        if (type == typeof(bool)) return 1;
        if (type == typeof(short) || type == typeof(ushort)) return 1;
        if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 2;
        throw new NotSupportedException($"不支持的类型: {type.Name}");
    }

    private static List<T> ConvertToTypeList<T>(int[] source, int elementCount)
    {
        var result = new List<T>();
        int wordSize = GetWordSize(typeof(T));

        for (int i = 0; i < elementCount; i++)
        {
            int idx = i * wordSize;
            object value = Type.GetTypeCode(typeof(T)) switch
            {
                TypeCode.Int16 => (short)(ushort)source[idx],
                TypeCode.UInt16 => (ushort)source[idx],
                TypeCode.Int32 => CombineToInt32(source[idx + 1], source[idx]),
                TypeCode.UInt32 => CombineToUInt32(source[idx + 1], source[idx]),
                TypeCode.Single => CombineToFloat(source[idx + 1], source[idx]),
                _ => throw new NotSupportedException($"不支持的类型: {typeof(T).Name}")
            };
            result.Add((T)value);
        }
        return result;
    }

    private static int[] ConvertToIntArray<T>(List<T> data, int wordSize)
    {
        var result = new int[data.Count * wordSize];
        for (int i = 0; i < data.Count; i++)
        {
            int idx = i * wordSize;
            switch (Type.GetTypeCode(typeof(T)))
            {
                case TypeCode.Int16:
                case TypeCode.UInt16:
                    result[idx] = Convert.ToInt32(data[i]);
                    break;
                case TypeCode.Int32:
                    SplitInt32(Convert.ToInt32(data[i]), out result[idx + 1], out result[idx]);
                    break;
                case TypeCode.UInt32:
                    SplitUInt32(Convert.ToUInt32(data[i]), out result[idx + 1], out result[idx]);
                    break;
                case TypeCode.Single:
                    SplitFloat(Convert.ToSingle(data[i]), out result[idx + 1], out result[idx]);
                    break;
                default:
                    throw new NotSupportedException($"不支持的类型: {typeof(T).Name}");
            }
        }
        return result;
    }

    private static int CombineToInt32(int high, int low)
        => ((ushort)high << 16) | (ushort)low;

    private static uint CombineToUInt32(int high, int low)
        => ((uint)(ushort)high << 16) | (ushort)low;

    private static float CombineToFloat(int high, int low)
    {
        byte[] bytes = [(byte)((ushort)high >> 8), (byte)(ushort)high,
                        (byte)((ushort)low >> 8),  (byte)(ushort)low];
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes, 0);
    }

    private static void SplitInt32(int value, out int high, out int low)
    {
        high = (value >> 16) & 0xFFFF;
        low = value & 0xFFFF;
    }

    private static void SplitUInt32(uint value, out int high, out int low)
    {
        high = (int)((value >> 16) & 0xFFFF);
        low = (int)(value & 0xFFFF);
    }

    private static void SplitFloat(float value, out int high, out int low)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        high = (bytes[0] << 8) | bytes[1];
        low = (bytes[2] << 8) | bytes[3];
    }
}