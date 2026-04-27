namespace IIoT.Edge.Application.Abstractions.Plc.Signals;

public enum ModuleSignalDirection
{
    Read = 0,
    Write = 1
}

public sealed record ModuleSignalDefinition(
    string Label,
    string DisplayName,
    string DefaultAddress,
    int AddressCount,
    string DataType,
    ModuleSignalDirection Direction,
    int SortOrder);
