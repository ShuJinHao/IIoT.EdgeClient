using System.Globalization;

namespace IIoT.Edge.Module.Homogenization.Resources;

public static class HomogenizationText
{
    public static string Get(string key, string fallback)
    {
        return string.IsNullOrWhiteSpace(fallback) ? key : fallback;
    }

    public static string Format(string key, string fallback, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key, fallback), args);
}
