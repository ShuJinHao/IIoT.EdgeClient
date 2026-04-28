using IIoT.Edge.SharedKernel.Domain;

namespace IIoT.Edge.Domain.Config.Aggregates;

public class SystemConfigEntity : BaseEntity<int>, IAggregateRoot
{
    protected SystemConfigEntity() { }

    private SystemConfigEntity(
        string key,
        string value,
        string? description)
    {
        Key = NormalizeRequired(key, "系统配置键不能为空。");
        UpdateValue(value);
        UpdateDescription(description);
    }

    /// <summary>参数键名，唯一（如 "Mes.Address"）</summary>
    public string Key { get; private set; } = null!;

    /// <summary>参数值（统一存字符串）</summary>
    public string Value { get; private set; } = null!;

    /// <summary>说明</summary>
    public string? Description { get; private set; }

    /// <summary>排序</summary>
    public int SortOrder { get; private set; }

    public static SystemConfigEntity Create(
        string key,
        string value,
        string? description = null)
        => new(key, value, description);

    public void UpdateValue(string value)
    {
        Value = value?.Trim() ?? string.Empty;
    }

    public void UpdateDescription(string? description)
    {
        Description = NormalizeOptional(description);
    }

    public void UpdateSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentException("系统配置排序不能小于 0。", nameof(sortOrder));
        }

        SortOrder = sortOrder;
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
