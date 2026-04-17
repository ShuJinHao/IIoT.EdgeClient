using System.Globalization;
using System.Windows.Data;

namespace IIoT.Edge.TestSimulator.Views;

/// <summary>bool 转换为 !bool，用于按钮 IsEnabled 绑定。</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

