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
        NetworkDeviceId = networkDeviceId;
        Label = label;
        PlcAddress = plcAddress;
        AddressCount = addressCount;
        DataType = dataType;
        Direction = direction;
        Category = string.IsNullOrWhiteSpace(category) ? "单点读数据" : category.Trim();
        GroupName = groupName ?? string.Empty;
        DisplayRole = displayRole ?? string.Empty;
    }

    public int NetworkDeviceId { get; set; }
    public string Label { get; set; } = null!;
    public string PlcAddress { get; set; } = null!;
    public int AddressCount { get; set; } = 1;
    public string DataType { get; set; } = "Int16";
    public string Direction { get; set; } = "Read";
    public string Category { get; set; } = "单点读数据";
    public string GroupName { get; set; } = string.Empty;
    public string DisplayRole { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string? Remark { get; set; }

    // 导航属性
    public NetworkDeviceEntity NetworkDevice { get; set; } = null!;
}
