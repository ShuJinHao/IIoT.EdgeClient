namespace IIoT.Edge.Infrastructure.Integration.Config;

internal static class HttpUrl
{
    public static bool TryCreateAbsoluteHttpUri(string? value, out Uri uri)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)
            || !HasExplicitScheme(trimmed)
            || !Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri)
            || !IsHttp(absoluteUri))
        {
            uri = null!;
            return false;
        }

        uri = absoluteUri;
        return true;
    }

    public static bool TryCreateHttpBaseUri(string? value, out Uri uri)
    {
        var trimmed = value?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed)
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var baseUri)
            && IsHttp(baseUri))
        {
            uri = baseUri;
            return true;
        }

        uri = null!;
        return false;
    }

    public static Uri Build(Uri baseUri, string relativeOrAbsoluteUrl)
    {
        var value = relativeOrAbsoluteUrl.Trim();
        if (TryCreateAbsoluteHttpUri(value, out var absoluteUri))
        {
            return absoluteUri;
        }

        if (HasExplicitScheme(value))
        {
            throw new InvalidOperationException($"URL 只允许 HTTP/HTTPS 或相对 API path：{relativeOrAbsoluteUrl}");
        }

        return new Uri(baseUri, value);
    }

    public static string GetRoutePath(string value)
    {
        var trimmed = value.Trim();
        if (TryCreateAbsoluteHttpUri(trimmed, out var uri))
        {
            return NormalizePath(uri.AbsolutePath);
        }

        var end = trimmed.IndexOfAny(['?', '#']);
        var path = end >= 0 ? trimmed[..end] : trimmed;
        return NormalizePath(path);
    }

    public static bool SamePath(string left, string right)
        => string.Equals(GetRoutePath(left), GetRoutePath(right), StringComparison.OrdinalIgnoreCase);

    public static bool HasExplicitScheme(string value)
    {
        var colonIndex = value.IndexOf(':');
        if (colonIndex <= 0)
        {
            return false;
        }

        var boundaryIndex = value.IndexOfAny(['/', '\\', '?', '#']);
        return (boundaryIndex < 0 || colonIndex < boundaryIndex)
            && Uri.CheckSchemeName(value[..colonIndex]);
    }

    public static bool IsHttp(Uri uri)
        => string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
    {
        var normalized = Uri.UnescapeDataString(path.Trim());
        var end = normalized.IndexOfAny(['?', '#']);
        if (end >= 0)
        {
            normalized = normalized[..end];
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "/";
        }

        return normalized.StartsWith('/')
            ? normalized
            : "/" + normalized;
    }
}
