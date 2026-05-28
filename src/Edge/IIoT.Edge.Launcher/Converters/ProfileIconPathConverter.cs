using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace IIoT.Edge.Launcher.Converters;

public sealed class ProfileIconPathConverter : IValueConverter
{
    private const string DefaultPath = "M4,6 L20,6 L20,18 L4,18 Z M7,9 L17,9 M7,13 L17,13 M9,18 L9,21 M15,18 L15,21";

    private static readonly IReadOnlyDictionary<string, string> IconPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["BeakerOutline"] = "M9,3 L15,3 M10,3 L10,9 L5,18 A2,2 0 0 0 7,21 L17,21 A2,2 0 0 0 19,18 L14,9 L14,3 M8,15 L16,15",
        ["Cog"] = "M12,4 L14,4 L14.5,6 L16.2,7 L18.2,6.2 L19.2,8 L17.6,9.4 L17.8,11.3 L20,12 L19.4,14 L17.2,13.8 L16,15.2 L16.4,17.4 L14.6,18.4 L13.2,16.8 L11.2,16.8 L9.8,18.4 L8,17.4 L8.4,15.2 L7.2,13.8 L5,14 L4.4,12 L6.2,11.3 L6.4,9.4 L4.8,8 L5.8,6.2 L7.8,7 L9.5,6 L10,4 Z M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9",
        ["ViewDashboardOutline"] = "M4,4 L20,4 L20,20 L4,20 Z M7,7 L12,7 L12,12 L7,12 Z M14,7 L17,7 L17,17 L14,17 Z M7,14 L12,14 L12,17 L7,17 Z"
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value?.ToString();
        if (string.IsNullOrWhiteSpace(key) || !IconPaths.TryGetValue(key, out var path))
        {
            path = DefaultPath;
        }

        return Geometry.Parse(path);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
