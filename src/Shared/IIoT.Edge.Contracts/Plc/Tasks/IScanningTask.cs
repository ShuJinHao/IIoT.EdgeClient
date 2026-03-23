namespace IIoT.Edge.Contracts.Plc.Tasks;

public interface IScanningTask : IPlcTask
{
    Task<string> ReadCodeAsync();
    bool Validate(string code);
}