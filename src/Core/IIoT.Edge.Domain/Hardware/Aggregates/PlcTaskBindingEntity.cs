using IIoT.Edge.SharedKernel.Domain;

namespace IIoT.Edge.Domain.Hardware.Aggregates;

public class PlcTaskBindingEntity : BaseEntity<int>, IAggregateRoot
{
    protected PlcTaskBindingEntity() { }

    public PlcTaskBindingEntity(
        int networkDeviceId,
        string taskKey,
        bool enabled,
        DateTimeOffset updatedAt)
    {
        BindNetworkDevice(networkDeviceId);
        ChangeTaskKey(taskKey);
        UpdateEnabled(enabled, updatedAt);
    }

    public int NetworkDeviceId { get; private set; }
    public string TaskKey { get; private set; } = null!;
    public bool Enabled { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public NetworkDeviceEntity NetworkDevice { get; private set; } = null!;

    public static PlcTaskBindingEntity Create(
        int networkDeviceId,
        string taskKey,
        bool enabled,
        DateTimeOffset updatedAt)
        => new(networkDeviceId, taskKey, enabled, updatedAt);

    public void BindNetworkDevice(int networkDeviceId)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException("PLC 任务绑定必须关联有效的网络设备。");
        }

        NetworkDeviceId = networkDeviceId;
    }

    public void ChangeTaskKey(string taskKey)
        => TaskKey = Require(taskKey, "PLC 任务 Key 不能为空。");

    public void UpdateEnabled(bool enabled, DateTimeOffset updatedAt)
    {
        Enabled = enabled;
        UpdatedAt = updatedAt;
    }

    private static string Require(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message);
        }

        return value.Trim();
    }
}
