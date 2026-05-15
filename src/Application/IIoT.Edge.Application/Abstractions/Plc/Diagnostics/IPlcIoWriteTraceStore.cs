namespace IIoT.Edge.Application.Abstractions.Plc.Diagnostics;

/// <summary>
/// PLC I/O 块写入轨迹类型，用于现场联调时串起 UI 申请、缓冲接收和扫描任务写入证据。
/// </summary>
public enum PlcIoWriteTraceKind
{
    Attempt = 0,
    Success = 1,
    Failed = 2
}

/// <summary>
/// PLC I/O 扫描任务的一次块写入轨迹，只记录只读诊断信息，不改变运行时写入策略。
/// </summary>
public sealed record PlcIoWriteTraceEntry(
    DateTimeOffset OccurredAt,
    PlcIoWriteTraceKind Kind,
    int DeviceId,
    string DeviceName,
    string StartAddress,
    int WordCount,
    IReadOnlyList<string> SignalKeys,
    string? ErrorMessage);

/// <summary>
/// PLC I/O 块写入轨迹存储，只服务运行联调诊断展示，不参与重试、补偿或业务判断。
/// </summary>
public interface IPlcIoWriteTraceStore
{
    void Record(PlcIoWriteTraceEntry entry);

    IReadOnlyList<PlcIoWriteTraceEntry> GetRecent(int count = 50);

    PlcIoWriteTraceEntry? GetLatestForSignals(int deviceId, IReadOnlyCollection<string> signalKeys);
}
