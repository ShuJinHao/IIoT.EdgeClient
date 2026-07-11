namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;

/// <summary>
/// 对单个 PLC 第三方 transport/protocol 实例提供唯一所有权和最多一次释放语义。
/// </summary>
internal sealed class PlcTransportOwner<T>
    where T : class
{
    private readonly Action<T> _release;
    private T? _value;

    public PlcTransportOwner(T value, Action<T> release)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(release);
        _value = value;
        _release = release;
    }

    public bool IsAvailable => Volatile.Read(ref _value) is not null;

    public T Value
        => Volatile.Read(ref _value)
           ?? throw new ObjectDisposedException(typeof(T).Name);

    public T? ValueOrDefault => Volatile.Read(ref _value);

    public void Release()
    {
        var value = Interlocked.Exchange(ref _value, null);
        if (value is not null)
        {
            _release(value);
        }
    }
}
