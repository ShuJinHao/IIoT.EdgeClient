using IIoT.Edge.Contracts;
using IIoT.Edge.Contracts.Plc;

namespace IIoT.Edge.PlcDevice.Proxy;

public class PlcServiceProxy : IPlcService
{
    private readonly IPlcService _target;
    private readonly ILogService _logger;
    private readonly string _deviceName;

    public bool IsConnected => _target.IsConnected;

    public PlcServiceProxy(IPlcService target, ILogService logger, string deviceName)
    {
        _target = target;
        _logger = logger;
        _deviceName = deviceName;
    }

    public void Init(string ip, int port)
        => _target.Init(ip, port);

    public bool Connect()
    {
        try
        {
            return _target.Connect();
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_deviceName}] 连接失败: {ex.Message}");
            throw;
        }
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            var result = await _target.ConnectAsync();
            if (!result)
                _logger.Warn($"[{_deviceName}] 连接失败");
            return result;
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_deviceName}] 连接异常: {ex.Message}");
            throw;
        }
    }

    public void Disconnect()
        => _target.Disconnect();

    public List<T> ReadData<T>(string address, ushort length)
    {
        try
        {
            return _target.ReadData<T>(address, length);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_deviceName}] 读取 {address} 失败: {ex.Message}");
            throw;
        }
    }

    public async Task<List<T>> ReadDataAsync<T>(string address, ushort length)
    {
        try
        {
            return await _target.ReadDataAsync<T>(address, length);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_deviceName}] 读取 {address} 失败: {ex.Message}");
            throw;
        }
    }

    public void WriteData<T>(string address, List<T> data)
    {
        try
        {
            _target.WriteData(address, data);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_deviceName}] 写入 {address} 失败: {ex.Message}");
            throw;
        }
    }

    public async Task WriteDataAsync<T>(string address, List<T> data)
    {
        try
        {
            await _target.WriteDataAsync(address, data);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_deviceName}] 写入 {address} 失败: {ex.Message}");
            throw;
        }
    }

    public void Dispose()
        => _target.Dispose();
}