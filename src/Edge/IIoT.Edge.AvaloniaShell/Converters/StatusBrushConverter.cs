using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace IIoT.Edge.AvaloniaShell.Converters;

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Running" => Brushes.DeepSkyBlue,
            "Connected" => Brushes.LimeGreen,
            "Warning" => Brushes.Gold,
            "Error" => Brushes.OrangeRed,
            _ => Brushes.Gray
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
