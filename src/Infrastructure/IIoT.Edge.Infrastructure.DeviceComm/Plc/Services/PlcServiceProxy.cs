using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;

public sealed class PlcServiceProxy : IPlcService
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

    public void Init(string ip, int port) => _target.Init(ip, port);

    public async Task<bool> ConnectAsync()
    {
        try
        {
            var result = await _target.ConnectAsync().ConfigureAwait(false);
            if (!result)
            {
                _logger.Warn($"[{_deviceName}] 杩炴帴澶辫触");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_deviceName}] 杩炴帴寮傚父: {ex.Message}");
            throw;
        }
    }

    public void Disconnect() => _target.Disconnect();

    public async Task<List<T>> ReadDataAsync<T>(string address, ushort length)
    {
        try
        {
            return await _target.ReadDataAsync<T>(address, length).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_deviceName}] 璇诲彇 {address} 澶辫触: {ex.Message}");
            throw;
        }
    }

    public async Task WriteDataAsync<T>(string address, List<T> data)
    {
        try
        {
            await _target.WriteDataAsync(address, data).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_deviceName}] 鍐欏叆 {address} 澶辫触: {ex.Message}");
            throw;
        }
    }

    public void Dispose() => _target.Dispose();
}
