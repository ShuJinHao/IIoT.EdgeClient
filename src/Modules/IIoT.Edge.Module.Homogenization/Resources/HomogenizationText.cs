using System.Globalization;
namespace IIoT.Edge.Module.Homogenization.Resources;

internal static class HomogenizationText
{
    public static string Get(string key, string fallback)
    {
        var app = Avalonia.Application.Current;
        if (app?.TryGetResource(key, null, out var value) == true
            && value is string text
            && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return fallback;
    }

    public static string Format(string key, string fallback, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key, fallback), args);
}
