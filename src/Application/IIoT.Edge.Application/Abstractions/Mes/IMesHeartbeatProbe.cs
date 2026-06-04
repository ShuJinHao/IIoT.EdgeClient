using IIoT.Edge.Application.Abstractions.Shared;

namespace IIoT.Edge.Application.Abstractions.Mes;

public interface IMesHeartbeatProbe
{
    Task<ExternalHeartbeatSnapshot> ProbeAsync(CancellationToken cancellationToken = default);
}
