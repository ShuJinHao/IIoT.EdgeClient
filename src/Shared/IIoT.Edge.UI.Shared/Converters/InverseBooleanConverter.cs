using System.Globalization;
using System.Windows.Data;

namespace IIoT.Edge.UI.Shared.Converters;

/// <summary>
/// 布尔值取反转换器。
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}
