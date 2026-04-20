namespace IIoT.Edge.Application.Abstractions.Plc;

/// <summary>
/// PLC 通信服务契约。
/// 统一定义 PLC 初始化、连接管理以及读写数据能力。
/// </summary>
public interface IPlcService
{
    bool IsConnected { get; }

    void Init(string ip, int port);

    Task<bool> ConnectAsync();

    void Disconnect();

    Task<List<T>> ReadDataAsync<T>(string address, ushort length);

    Task WriteDataAsync<T>(string address, List<T> data);

    void Dispose();
}
