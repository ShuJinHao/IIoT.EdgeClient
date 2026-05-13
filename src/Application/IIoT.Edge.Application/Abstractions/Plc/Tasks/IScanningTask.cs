namespace IIoT.Edge.Application.Abstractions.Plc.Tasks;

/// <summary>
/// 扫码任务契约。
/// </summary>
public interface IScanningTask : IPlcTask
{
    Task<string> ReadCodeAsync();
    bool Validate(string code);
}
