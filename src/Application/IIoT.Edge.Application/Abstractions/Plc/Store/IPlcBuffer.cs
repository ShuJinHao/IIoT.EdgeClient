namespace IIoT.Edge.Application.Abstractions.Plc.Store;

/// <summary>
/// PLC 缓冲区内的信号值变更事件。
/// </summary>
public sealed class PlcSignalBufferChangedEventArgs : EventArgs
{
    public PlcSignalBufferChangedEventArgs(string signalKey, string direction)
    {
        SignalKey = signalKey;
        Direction = direction;
    }

    public string SignalKey { get; }

    public string Direction { get; }
}

/// <summary>
/// PLC 缓冲区中一个业务信号与运行态数组下标的绑定。
/// </summary>
public sealed record PlcBufferSignalBinding(
    string SignalKey,
    string Direction,
    int Offset,
    int AddressCount);

/// <summary>
/// PLC 信号写入快照，供信号交互循环按地址块批量写入 PLC。
/// </summary>
public sealed record PlcSignalWriteSnapshot(
    string SignalKey,
    IReadOnlyList<ushort> Words);

/// <summary>
/// PLC 缓冲区抽象。新业务优先按 SignalKey 读写，旧下标方法仅作为过渡兼容入口。
/// </summary>
public interface IPlcBuffer
{
    event EventHandler<PlcSignalBufferChangedEventArgs>? SignalValuesChanged;

    ushort GetReadValue(int index);

    bool TryGetReadWords(string signalKey, out ushort[] values);

    bool TryGetWriteWords(string signalKey, out ushort[] values);

    void SetWriteValue(int index, ushort value);

    void SetWriteValue(string signalKey, int offset, ushort value);
}

/// <summary>
/// PLC 缓冲区传输抽象，供信号交互循环批量搬运 PLC 数据。
/// </summary>
public interface IPlcBufferTransport : IPlcBuffer
{
    void UpdateReadBuffer(ushort[] data);

    void UpdateReadSignal(string signalKey, IReadOnlyList<ushort> data);

    ushort[] GetWriteBuffer();

    void SetSignalBindings(IReadOnlyCollection<PlcBufferSignalBinding> bindings);
}

/// <summary>
/// PLC 读信号新鲜度查询，用于只读采样上传避免把旧 buffer 当作新数据。
/// </summary>
public interface IPlcReadSignalFreshness
{
    bool TryGetReadSignalUpdatedAt(string signalKey, out DateTimeOffset updatedAt);
}
