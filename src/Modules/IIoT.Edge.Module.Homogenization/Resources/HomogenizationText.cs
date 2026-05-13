using System.Globalization;
using WpfApplication = System.Windows.Application;

namespace IIoT.Edge.Module.Homogenization.Resources;

internal static class HomogenizationText
{
    public static string Get(string key, string fallback)
    {
        var value = WpfApplication.Current?.TryFindResource(key) as string;
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public static string Format(string key, string fallback, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key, fallback), args);
}
