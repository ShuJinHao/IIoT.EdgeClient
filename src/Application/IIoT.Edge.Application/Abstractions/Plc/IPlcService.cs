namespace IIoT.Edge.Application.Abstractions.Plc;

/// <summary>
/// PLC 通信服务契约。
/// 统一定义 PLC 初始化、连接管理以及读写数据能力。
/// </summary>
public interface IPlcService : IAsyncDisposable
{
    bool IsConnected { get; }

    void Init(PlcEndpoint endpoint);

    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<List<T>> ReadDataAsync<T>(
        string address,
        ushort length,
        CancellationToken cancellationToken = default);

    Task WriteDataAsync<T>(
        string address,
        List<T> data,
        CancellationToken cancellationToken = default);
}
