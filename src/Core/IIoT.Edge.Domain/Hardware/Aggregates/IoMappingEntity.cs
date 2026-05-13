using IIoT.Edge.SharedKernel.Domain;

namespace IIoT.Edge.Domain.Hardware.Aggregates;

public class IoMappingEntity : BaseEntity<int>, IAggregateRoot
{
    private const string DefaultCategory = "单点读数据";

    protected IoMappingEntity() { }

    public IoMappingEntity(
        int networkDeviceId,
        string signalKey,
        string plcAddress,
        int addressCount,
        string dataType,
        string direction,
        string category = DefaultCategory,
        string businessGroup = "",
        string signalName = "")
    {
        BindNetworkDevice(networkDeviceId);
        UpdateAddress(plcAddress, addressCount);
        UpdateMetadata(signalKey, dataType, direction, category, businessGroup, signalName, null);
    }

    public int NetworkDeviceId { get; private set; }
    public string SignalKey { get; private set; } = null!;
    public string PlcAddress { get; private set; } = null!;
    public int AddressCount { get; private set; } = 1;
    public string DataType { get; private set; } = "Int16";
    public string Direction { get; private set; } = "Read";
    public string Category { get; private set; } = DefaultCategory;
    public string BusinessGroup { get; private set; } = string.Empty;
    public string SignalName { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }
    public string? Remark { get; private set; }

    public NetworkDeviceEntity NetworkDevice { get; private set; } = null!;

    public static IoMappingEntity Create(
        int networkDeviceId,
        string signalKey,
        string plcAddress,
        int addressCount,
        string dataType,
        string direction,
        string category = DefaultCategory,
        string businessGroup = "",
        string signalName = "")
        => new(networkDeviceId, signalKey, plcAddress, addressCount, dataType, direction, category, businessGroup, signalName);

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
        string signalKey,
        string dataType,
        string direction,
        string? category,
        string? businessGroup,
        string? signalName,
        string? remark)
    {
        SignalKey = Require(signalKey, "IO 内部信号键不能为空。");
        DataType = Require(dataType, "IO 数据类型不能为空。");
        Direction = Require(direction, "IO 方向不能为空。");
        Category = string.IsNullOrWhiteSpace(category) ? DefaultCategory : category.Trim();
        BusinessGroup = NormalizeToEmpty(businessGroup);
        SignalName = NormalizeToEmpty(signalName);
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
