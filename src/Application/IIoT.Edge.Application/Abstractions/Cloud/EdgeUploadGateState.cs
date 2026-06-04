namespace IIoT.Edge.Application.Abstractions.Cloud;

public enum EdgeUploadGateState
{
    Unknown = 0,
    Refreshing = 1,
    Ready = 2,
    Blocked = 3
}
