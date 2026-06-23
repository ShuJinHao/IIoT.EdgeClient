namespace IIoT.Edge.Application.Abstractions.Plc.Signals;

/// <summary>
/// 按插件强类型信号键读写 PLC 缓冲区，运行任务不得直接使用字符串 SignalKey 访问点位。
/// </summary>
/// <typeparam name="TSignalKey">插件声明的 PLC 信号枚举。</typeparam>
public interface ILogicalSignalAccessor<TSignalKey>
    where TSignalKey : struct, Enum
{
    bool CanRead(TSignalKey key);

    bool CanWrite(TSignalKey key);

    bool TryReadUInt16(TSignalKey key, out ushort value);

    ushort ReadUInt16(TSignalKey key);

    short ReadInt16(TSignalKey key);

    uint ReadUInt32(TSignalKey key);

    int ReadInt32(TSignalKey key);

    string ReadAscii(TSignalKey key);

    IReadOnlyList<int> ReadIntArray(TSignalKey key, int count);

    IReadOnlyList<bool> ReadBoolArray(TSignalKey key, int count);

    IReadOnlyList<double> ReadFloatArray(TSignalKey key, int count);

    void WriteUInt16(TSignalKey key, ushort value);
}
