using IIoT.Edge.SharedKernel.Domain;

namespace IIoT.Edge.Domain.Hardware.Aggregates;

public class IoMappingEntity : BaseEntity<int>, IAggregateRoot
{
    protected IoMappingEntity() { }

    public IoMappingEntity(
        int networkDeviceId,
        string label,
        string plcAddress,
        int addressCount,
        string dataType,
        string direction,
        string category = "单点读数据",
        string groupName = "",
        string displayRole = "")
    {
        BindNetworkDevice(networkDeviceId);
        UpdateAddress(plcAddress, addressCount);
        UpdateMetadata(label, dataType, direction, category, groupName, displayRole, null);
    }

    public int NetworkDeviceId { get; private set; }
    public string Label { get; private set; } = null!;
    public string PlcAddress { get; private set; } = null!;
    public int AddressCount { get; private set; } = 1;
    public string DataType { get; private set; } = "Int16";
    public string Direction { get; private set; } = "Read";
    public string Category { get; private set; } = "单点读数据";
    public string GroupName { get; private set; } = string.Empty;
    public string DisplayRole { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public string? Remark { get; private set; }

    public NetworkDeviceEntity NetworkDevice { get; private set; } = null!;

    public static IoMappingEntity Create(
        int networkDeviceId,
        string label,
        string plcAddress,
        int addressCount,
        string dataType,
        string direction,
        string category = "单点读数据",
        string groupName = "",
        string displayRole = "")
        => new(networkDeviceId, label, plcAddress, addressCount, dataType, direction, category, groupName, displayRole);

    public void BindNetworkDevice(int networkDeviceId)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException("IO 映射必须绑定有效的网络设备。");
        }

        NetworkDeviceId = networkDeviceId;
    }

    public void UpdateAddress(string plcAddress, int addressCount)
    {
        PlcAddress = Require(plcAddress, "PLC 地址不能为空。");
        if (addressCount <= 0)
        {
            throw new ArgumentException("地址数量必须大于 0。");
        }

        AddressCount = addressCount;
    }

    public void UpdateMetadata(
        string label,
        string dataType,
        string direction,
        string? category,
        string? groupName,
        string? displayRole,
        string? remark)
    {
        Label = Require(label, "IO 标签不能为空。");
        DataType = Require(dataType, "IO 数据类型不能为空。");
        Direction = Require(direction, "IO 方向不能为空。");
        Category = string.IsNullOrWhiteSpace(category) ? "单点读数据" : category.Trim();
        GroupName = NormalizeToEmpty(groupName);
        DisplayRole = NormalizeToEmpty(displayRole);
        Remark = Normalize(remark);
    }

    public void UpdateSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentException("IO 映射排序不能小于 0。");
        }

        SortOrder = sortOrder;
    }

    private static string Require(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message);
        }

        return value.Trim();
    }

    private static string NormalizeToEmpty(string? value)
        => value?.Trim() ?? string.Empty;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
