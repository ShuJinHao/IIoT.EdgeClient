using IIoT.Edge.Module.Contracts.Plugins;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Identity;

namespace IIoT.Edge.Application.Common.Plugins;

/// <summary>
/// 正式 v3 Host 对当前插件配置的进程内快照端口。运行热路径只能读取这里已经发布的快照；
/// 只有启动生命周期和成功写入后的受控刷新可以触碰插件配置存储。
/// </summary>
public interface IDevicePluginConfigurationSnapshotAccessor
{
    bool IsInitialized { get; }

    DevicePluginConfigurationSnapshot GetRequiredSnapshot();

    IReadOnlyList<DevicePluginPlcSnapshot> GetPlcs();

    IReadOnlyList<DevicePluginIoPointSnapshot> GetIoPoints();

    IReadOnlyList<DevicePluginTaskBindingSnapshot> GetTaskBindings();

    Task RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed record DevicePluginPlcSnapshot(
    int Id,
    DevicePluginPlcConfiguration Configuration) : IDeviceIdentifiable, IPlcIdentifiable
{
    public int NetworkDeviceId => Id;
    public string DeviceName => Configuration.DisplayName;
    public string PlcCode => Configuration.PlcCode;
    public DeviceType DeviceType => IIoT.Edge.Module.Contracts.Hardware.DeviceType.PLC;
    public string? DeviceModel => Configuration.DeviceModel ?? Configuration.DeviceType;
    public string? ProtocolFrame => Configuration.ProtocolFrame;
    public string IpAddress => Configuration.IpAddress;
    public int Port1 => Configuration.PrimaryPort;
    public int? Port2 => Configuration.SecondaryPort;
    public string? SendCmd1 => null;
    public string? SendCmd2 => null;
    public int ConnectTimeout => Configuration.ConnectTimeoutMilliseconds;
    public bool IsEnabled => Configuration.IsEnabled;
    public string? Remark => Configuration.Remark;
}

public sealed record DevicePluginIoPointSnapshot(
    int Id,
    int NetworkDeviceId,
    DevicePluginIoPointConfiguration Configuration)
{
    public string PlcCode => Configuration.PlcCode;
    public string SignalKey => Configuration.SignalKey;
    public string PlcAddress => Configuration.PlcAddress;
    public int AddressCount => Configuration.AddressCount;
    public string DataType => Configuration.DataType;
    public string Direction => Configuration.Direction;
    public string Category => Configuration.Category;
    public string BusinessGroup => Configuration.BusinessGroup;
    public int SortOrder => Configuration.SortOrder;
    public string? Remark => Configuration.Remark;
}

public sealed record DevicePluginTaskBindingSnapshot(
    int Id,
    int NetworkDeviceId,
    DevicePluginTaskBindingConfiguration Configuration)
{
    public string PlcCode => Configuration.PlcCode;
    public string TaskKey => Configuration.TaskKey;
    public bool Enabled => Configuration.Enabled;
    public DateTimeOffset UpdatedAt => Configuration.UpdatedAtUtc;
}

public static class DevicePluginProjectionIds
{
    public static int Plc(string plcCode) => Stable($"plc\u001f{plcCode}");

    public static int Io(string plcCode, string signalKey)
        => Stable($"io\u001f{plcCode}\u001f{signalKey}");

    public static int Binding(string plcCode, string taskKey)
        => Stable($"binding\u001f{plcCode}\u001f{taskKey}");

    public static int Setting(string key) => Stable($"setting\u001f{key}");

    private static int Stable(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var character in value.ToUpperInvariant())
        {
            hash ^= character;
            hash *= prime;
        }

        var result = (int)(hash & 0x7fffffff);
        return result == 0 ? 1 : result;
    }
}
