namespace IIoT.Edge.Contracts.Plc;

public interface ISignalInteraction : IPlcTask
{
    bool IsConnected { get; }
    Task ConnectAsync();
}