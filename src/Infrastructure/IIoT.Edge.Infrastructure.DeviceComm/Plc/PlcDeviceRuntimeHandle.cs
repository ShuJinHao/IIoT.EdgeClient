using IIoT.Edge.Module.Contracts.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcDeviceRuntimeHandle
{
    private readonly object _stopLock = new();
    private Task? _stopTask;
    private int _cancellationDisposed;

    public required int DeviceId { get; init; }

    public required string DeviceName { get; init; }

    public required IPlcService PlcService { get; init; }

    public required CancellationTokenSource CancellationTokenSource { get; init; }

    public required IReadOnlyList<IPlcTask> Tasks { get; init; }

    public List<Task> RunningHandles { get; } = new();

    public Task RequestStopAsync()
    {
        lock (_stopLock)
        {
            return _stopTask ??= CancellationTokenSource.CancelAsync();
        }
    }

    public void DisposeCancellation()
    {
        if (Interlocked.Exchange(ref _cancellationDisposed, 1) == 0)
        {
            CancellationTokenSource.Dispose();
        }
    }
}
