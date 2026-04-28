using System.Collections.Concurrent;
using System.Text.Json;

namespace IIoT.Edge.SharedKernel.DataPipeline.CellData;

/// <summary>
/// 电芯数据类型注册表。
/// 集中维护 processType 字符串到 CellDataBase 子类的映射关系。
/// 消除各处硬编码的 switch 表达式，新增工序只需注册一次。
/// </summary>
public static class CellDataTypeRegistry
{
    private static readonly ConcurrentDictionary<string, Type> _map = new();

    /// <summary>
    /// 注册一种工序类型。
    /// </summary>
    public static void Register<T>(string processType) where T : CellDataBase
        => _map[processType] = typeof(T);

    public static void Register(string processType, Type cellDataType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processType);
        ArgumentNullException.ThrowIfNull(cellDataType);

        if (!typeof(CellDataBase).IsAssignableFrom(cellDataType))
        {
            throw new InvalidOperationException(
                $"CellData type '{cellDataType.FullName}' must inherit from {nameof(CellDataBase)}.");
        }

        _map[processType] = cellDataType;
    }

    /// <summary>
    /// 根据 processType 获取对应的 CLR 类型。
    /// </summary>
    public static Type? Resolve(string processType)
        => _map.GetValueOrDefault(processType);

    /// <summary>
    /// 根据 processType 反序列化 JSON 为对应的 CellDataBase 子类。
    /// 如果 processType 未注册，返回 null。
    /// </summary>
    public static CellDataBase? Deserialize(string processType, string json, JsonSerializerOptions? options = null)
    {
        var type = Resolve(processType);
        if (type is null) return null;

        return (CellDataBase?)JsonSerializer.Deserialize(json, type, options);
    }
}
