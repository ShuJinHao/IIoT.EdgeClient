using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Application.Abstractions.Plc;

public interface IPlcConnectionManager : IDisposable, IAsyncDisposable
{
    Task InitializeAsync(CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);

    Task ReloadAsync(string deviceName, CancellationToken ct = default);

    Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default);

    void RegisterTasks(
        string deviceName,
        Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory);

    IPlcService? GetPlc(int networkDeviceId);

    ProductionContext? GetContext(string deviceName);

    void MarkRuntimeFault(int networkDeviceId, string deviceName, string error) { }

    PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId) => null;

    IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses() => Array.Empty<PlcConnectionRuntimeSnapshot>();
}
