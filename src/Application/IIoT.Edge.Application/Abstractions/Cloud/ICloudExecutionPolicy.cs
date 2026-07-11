namespace IIoT.Edge.Application.Abstractions.Cloud;

/// <summary>
/// 当前 machine profile 的 Cloud 通信总门。关闭时任何 Cloud 请求都不得离开客户端。
/// </summary>
public interface ICloudExecutionPolicy
{
    bool IsEnabled { get; }
}
