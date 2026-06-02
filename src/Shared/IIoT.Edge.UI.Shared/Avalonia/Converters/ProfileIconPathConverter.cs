using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Converters;

public sealed class ProfileIconPathConverter : IValueConverter
{
    private const string DefaultResourceKey = "Edge.Icon.Profile.Default";

    private static readonly IReadOnlyDictionary<string, string> IconResourceKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["BeakerOutline"] = "Edge.Icon.Profile.BeakerOutline",
        ["Cog"] = "Edge.Icon.Profile.Cog",
        ["ViewDashboardOutline"] = "Edge.Icon.Profile.ViewDashboardOutline"
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value?.ToString();
        var resourceKey = !string.IsNullOrWhiteSpace(key) && IconResourceKeys.TryGetValue(key, out var matchedResourceKey)
            ? matchedResourceKey
            : DefaultResourceKey;

        return Application.Current?.TryGetResource(resourceKey, null, out var valueFromResources) == true
            && valueFromResources is Geometry geometry
                ? geometry
                : null!;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
