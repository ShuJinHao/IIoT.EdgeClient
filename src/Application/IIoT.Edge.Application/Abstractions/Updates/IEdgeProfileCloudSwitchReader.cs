namespace IIoT.Edge.Application.Abstractions.Updates;

/// <summary>
/// 读取指定 machine profile 的唯一 Cloud 系统开关。
/// </summary>
public interface IEdgeProfileCloudSwitchReader
{
    bool IsEnabled(EdgeUpdateTarget target);
}
