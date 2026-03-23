namespace IIoT.Edge.Contracts.Plc;

public interface IPlcTask
{
    string TaskName { get; }
    Task StartAsync(CancellationToken ct);
}