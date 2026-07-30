using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;

public sealed class PlcServiceProxy : IPlcService
{
    private readonly IPlcService _target;
    private readonly ILogService _logger;
    private readonly string _plcCode;

    public bool IsConnected => _target.IsConnected;

    public PlcServiceProxy(IPlcService target, ILogService logger, string plcCode)
    {
        _target = target;
        _logger = logger;
        _plcCode = plcCode;
    }

    public void Init(PlcEndpoint endpoint) => _target.Init(endpoint);

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteLoggedAsync(
                "连接异常",
                () => _target.ConnectAsync(cancellationToken))
            .ConfigureAwait(false);
        if (!result)
        {
            _logger.Warn($"[PlcCode={_plcCode}] 连接失败");
        }

        return result;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => _target.DisconnectAsync(cancellationToken);

    public async Task<List<T>> ReadDataAsync<T>(
        string address,
        ushort length,
        CancellationToken cancellationToken = default)
        => await ExecuteLoggedAsync(
                $"读取 {address} 失败",
                () => _target.ReadDataAsync<T>(address, length, cancellationToken))
            .ConfigureAwait(false);

    public async Task WriteDataAsync<T>(
        string address,
        List<T> data,
        CancellationToken cancellationToken = default)
        => await ExecuteLoggedAsync(
                $"写入 {address} 失败",
                async () =>
                {
                    await _target.WriteDataAsync(address, data, cancellationToken).ConfigureAwait(false);
                    return true;
                })
            .ConfigureAwait(false);

    public ValueTask DisposeAsync() => _target.DisposeAsync();

    private async Task<TResult> ExecuteLoggedAsync<TResult>(
        string failureMessage,
        Func<Task<TResult>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PlcServiceQuarantinedException ex)
        {
            _logger.Error($"[PlcCode={_plcCode}] PLC service 已隔离: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"[PlcCode={_plcCode}] {failureMessage}: {ex.Message}");
            throw;
        }
    }
}
