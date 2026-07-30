using IIoT.Edge.Module.Contracts.UI;

namespace IIoT.Edge.Application.Common.Identity;

/// <summary>
/// Host-only extension of the shared display selection with its resolved stable PLC identity.
/// </summary>
public interface IPlcDeviceSelectionContext : IDeviceSelectionContext
{
    string? SelectedPlcCode { get; }
}
