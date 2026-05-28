using System.Globalization;
using Avalonia.Data.Converters;
using IIoT.Edge.UI.Shared.Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Converters;

/// <summary>
/// 将日志等级字符串映射为共享视觉状态，仅用于 UI 颜色表达。
/// </summary>
public sealed class LogLevelToEdgeVisualStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value?.ToString();

        if (string.IsNullOrWhiteSpace(level))
        {
            return EdgeVisualStatus.Default;
        }

        return level.Trim() switch
        {
            var x when x.Equals("ERROR", StringComparison.OrdinalIgnoreCase)
                || x.Equals("FATAL", StringComparison.OrdinalIgnoreCase) => EdgeVisualStatus.Error,
            var x when x.Equals("WARN", StringComparison.OrdinalIgnoreCase)
                || x.Equals("WARNING", StringComparison.OrdinalIgnoreCase) => EdgeVisualStatus.Warning,
            var x when x.Equals("INFO", StringComparison.OrdinalIgnoreCase) => EdgeVisualStatus.Info,
            var x when x.Equals("DEBUG", StringComparison.OrdinalIgnoreCase)
                || x.Equals("TRACE", StringComparison.OrdinalIgnoreCase) => EdgeVisualStatus.Default,
            _ => EdgeVisualStatus.Default
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
