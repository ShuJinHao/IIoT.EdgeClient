using System.Globalization;

namespace IIoT.Edge.Module.Homogenization.Resources;

public static class HomogenizationText
{
    public static string Get(string key, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(key)
            && global::Avalonia.Application.Current?.Resources.TryGetValue(key, out var resource) == true
            && resource is string text
            && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return string.IsNullOrWhiteSpace(fallback) ? key : fallback;
    }

    public static string Format(string key, string fallback, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key, fallback), args);
}
