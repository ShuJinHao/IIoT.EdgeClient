using IIoT.Edge.Module.Contracts.Plc.Store;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;

internal interface IPlcOnDemandReadCoordinator
{
    bool Handles(IReadOnlyCollection<string> requiredSignalKeys);

    bool TryCapture(
        IReadOnlyCollection<string> requiredSignalKeys,
        out PlcReadBatchSnapshot? snapshot);
}
