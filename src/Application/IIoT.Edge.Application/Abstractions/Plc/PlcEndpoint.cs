namespace IIoT.Edge.Application.Abstractions.Plc;

/// <summary>
/// PLC 通信端点。TCP 和串口端点分开建模，避免把 RTU 串口号伪装成 IP 地址。
/// </summary>
public abstract record PlcEndpoint(int ConnectTimeoutMs)
{
    public TimeSpan ConnectTimeout
        => TimeSpan.FromMilliseconds(ConnectTimeoutMs <= 0 ? 3000 : ConnectTimeoutMs);
}

/// <summary>
/// 三菱 MC 协议帧类型。
/// </summary>
public enum McPlcFrameType
{
    E3,
    E4
}

/// <summary>
/// 基于 TCP 的 PLC 通信端点。
/// </summary>
public sealed record TcpPlcEndpoint(
    string Host,
    int Port,
    int ConnectTimeoutMs = 3000,
    McPlcFrameType McFrameType = McPlcFrameType.E3) : PlcEndpoint(ConnectTimeoutMs);

/// <summary>
/// 基于串口的 PLC 通信端点，供 Modbus RTU 使用。
/// </summary>
public sealed record SerialPlcEndpoint(
    string PortName,
    int BaudRate,
    int DataBits,
    string StopBits,
    string Parity,
    byte SlaveId = 1,
    int ConnectTimeoutMs = 3000) : PlcEndpoint(ConnectTimeoutMs);
