using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Resources;

namespace IIoT.Edge.Module.Homogenization.Config;

/// <summary>
/// 匀浆 Cloud 日志映射码表配置。Cloud 只接收日志语义，不承载 MES 设备状态模型。
/// </summary>
public sealed class HomogenizationCloudCodeOptions
{
    /// <summary>
    /// PLC 设备状态码到 Cloud 日志级别的映射，允许值为 INFO、WARN、ERROR。
    /// </summary>
    public Dictionary<string, string> EquipmentStatusLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按配置将设备状态转换为 Cloud 设备日志级别，未配置时使用插件默认判定。
    /// </summary>
    public string ResolveEquipmentStatusLevel(HomogenizationEquipmentStatusSnapshot snapshot)
    {
        if (EquipmentStatusLevels.TryGetValue(snapshot.StatusCode.ToString(), out var configured))
        {
            return NormalizeLevel(configured, GetDefaultStatusLevel(snapshot));
        }

        return GetDefaultStatusLevel(snapshot);
    }

    internal void AppendValidationErrors(ICollection<string> errors)
    {
        foreach (var item in EquipmentStatusLevels)
        {
            if (!IsSupportedLevel(item.Value))
            {
                errors.Add(HomogenizationText.Format(
                    "Homogenization_Validate_CloudStatusLevelInvalidFormat",
                    "Cloud 设备状态级别 {0}={1} 无效，只能配置 INFO、WARN 或 ERROR。",
                    item.Key,
                    item.Value));
            }
        }
    }

    private static string GetDefaultStatusLevel(HomogenizationEquipmentStatusSnapshot snapshot)
    {
        if (snapshot.StatusCode < 0)
        {
            return "ERROR";
        }

        if (snapshot.Messages.Count > 0
            || ContainsWarningWord(snapshot.StatusText))
        {
            return "WARN";
        }

        return "INFO";
    }

    private static bool ContainsWarningWord(string value)
        => value.Contains("报警", StringComparison.Ordinal)
           || value.Contains("异常", StringComparison.Ordinal)
           || value.Contains("故障", StringComparison.Ordinal)
           || value.Contains("离线", StringComparison.Ordinal);

    private static string NormalizeLevel(string? value, string fallback)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return IsSupportedLevel(normalized) ? normalized! : fallback;
    }

    private static bool IsSupportedLevel(string? value)
        => string.Equals(value, "INFO", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "WARN", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "ERROR", StringComparison.OrdinalIgnoreCase);
}
