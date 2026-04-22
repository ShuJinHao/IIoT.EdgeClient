using IIoT.Edge.Application.Abstractions.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcDeviceRuntimeHandle
{
    public required int DeviceId { get; init; }

    public required string DeviceName { get; init; }

    public required IPlcService PlcService { get; init; }

    public required CancellationTokenSource CancellationTokenSource { get; init; }

    public required IReadOnlyList<IPlcTask> Tasks { get; init; }

    public List<Task> RunningHandles { get; } = new();
}
