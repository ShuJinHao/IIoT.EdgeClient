namespace IIoT.Edge.Contracts.Plc.Tasks;

public interface IVoltageTestTask : IPlcTask
{
    Task<double> ReadVoltageAsync();
    bool IsInRange(double voltage);
}