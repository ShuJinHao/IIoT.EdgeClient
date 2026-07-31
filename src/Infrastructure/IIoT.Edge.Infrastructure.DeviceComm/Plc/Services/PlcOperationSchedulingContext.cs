namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services;

internal enum PlcOperationPriority
{
    Interactive = 0,
    BusinessOnDemand = 1,
    Periodic = 2
}

/// <summary>
/// 将调用方的 PLC 操作优先级传入同一 service 的操作门。未显式声明的连接、断开和
/// 手工操作按交互优先级处理；老化窗口始终限制在 500ms—5s。
/// </summary>
internal static class PlcOperationSchedulingContext
{
    private static readonly AsyncLocal<ScopeState?> CurrentState = new();
    private static readonly TimeSpan MinimumAgingInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaximumAgingInterval = TimeSpan.FromSeconds(5);

    public static PlcOperationSchedulingRequest Current
        => CurrentState.Value?.Request
           ?? new PlcOperationSchedulingRequest(
               PlcOperationPriority.Interactive,
               MinimumAgingInterval);

    public static IDisposable Push(PlcOperationPriority priority, TimeSpan agingInterval)
    {
        var previous = CurrentState.Value;
        var request = new PlcOperationSchedulingRequest(
            priority,
            ClampAgingInterval(agingInterval));
        CurrentState.Value = new ScopeState(request, previous);
        return new Scope(previous);
    }

    public static TimeSpan ClampAgingInterval(TimeSpan agingInterval)
    {
        if (agingInterval < MinimumAgingInterval)
        {
            return MinimumAgingInterval;
        }

        return agingInterval > MaximumAgingInterval
            ? MaximumAgingInterval
            : agingInterval;
    }

    private sealed record ScopeState(
        PlcOperationSchedulingRequest Request,
        ScopeState? Previous);

    private sealed class Scope(ScopeState? previous) : IDisposable
    {
        private ScopeState? _previous = previous;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            CurrentState.Value = Interlocked.Exchange(ref _previous, null);
        }
    }
}

internal readonly record struct PlcOperationSchedulingRequest(
    PlcOperationPriority Priority,
    TimeSpan AgingInterval);
