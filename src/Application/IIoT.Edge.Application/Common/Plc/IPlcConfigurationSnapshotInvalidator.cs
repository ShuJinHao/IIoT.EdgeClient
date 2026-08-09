namespace IIoT.Edge.Application.Common.Plc;

/// <summary>
/// Invalidates and refreshes the authoritative PLC configuration snapshot after a
/// committed hardware configuration mutation. Implementations must prevent an
/// older concurrent load from republishing stale configuration.
/// </summary>
public interface IPlcConfigurationSnapshotInvalidator
{
    void Invalidate();

    Task WarmAsync(CancellationToken cancellationToken = default);
}
