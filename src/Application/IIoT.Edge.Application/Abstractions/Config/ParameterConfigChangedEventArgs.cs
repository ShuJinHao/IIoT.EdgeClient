namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 本地参数配置变更事件。
/// </summary>
public sealed class ParameterConfigChangedEventArgs : EventArgs
{
    public ParameterConfigChangedEventArgs(
        ParameterConfigChangeScope scope,
        int? deviceId = null)
    {
        Scope = scope;
        DeviceId = deviceId;
    }

    public ParameterConfigChangeScope Scope { get; }

    public int? DeviceId { get; }
}
