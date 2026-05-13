using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Domain.Config.Aggregates;

public class DeviceParamEntity : BaseEntity<int>, IAggregateRoot
{
    protected DeviceParamEntity() { }

    private DeviceParamEntity(
        int networkDeviceId,
        string name,
        string value,
        string? unit = null)
    {
        BindNetworkDevice(networkDeviceId);
        UpdateMetadata(name, unit);
        UpdateValue(value);
    }

    /// <summary>外键，关联 NetworkDeviceEntity</summary>
    public int NetworkDeviceId { get; private set; }

    /// <summary>参数名（如"切刀速度"）</summary>
    public string Name { get; private set; } = null!;

    /// <summary>当前值</summary>
    public string Value { get; private set; } = null!;

    /// <summary>单位</summary>
    public string? Unit { get; private set; }

    /// <summary>下限</summary>
    public string? MinValue { get; private set; }

    /// <summary>上限</summary>
    public string? MaxValue { get; private set; }

    /// <summary>排序</summary>
    public int SortOrder { get; private set; }

    // 导航属性
    public NetworkDeviceEntity NetworkDevice { get; private set; } = null!;

    public static DeviceParamEntity Create(
        int networkDeviceId,
        string name,
        string value,
        string? unit = null)
        => new(networkDeviceId, name, value, unit);

    public void UpdateValue(string value)
    {
        Value = value?.Trim() ?? string.Empty;
    }

    public void UpdateBounds(string? minValue, string? maxValue)
    {
        MinValue = NormalizeOptional(minValue);
        MaxValue = NormalizeOptional(maxValue);
    }

    public void UpdateMetadata(string name, string? unit)
    {
        Name = NormalizeRequired(name, "设备参数名不能为空。");
        Unit = NormalizeOptional(unit);
    }

    public void UpdateSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentException("设备参数排序不能小于 0。", nameof(sortOrder));
        }

        SortOrder = sortOrder;
    }

    private void BindNetworkDevice(int networkDeviceId)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException("设备参数必须绑定有效设备。", nameof(networkDeviceId));
        }

        NetworkDeviceId = networkDeviceId;
    }

    private static string NormalizeRequired(string value, string message)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException(message);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
