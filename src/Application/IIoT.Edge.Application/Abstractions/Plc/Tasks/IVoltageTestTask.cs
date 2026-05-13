namespace IIoT.Edge.Application.Abstractions.Plc.Tasks;

/// <summary>
/// 电压测试任务契约。
/// </summary>
public interface IVoltageTestTask : IPlcTask
{
    Task<double> ReadVoltageAsync();
    bool IsInRange(double voltage);
}
