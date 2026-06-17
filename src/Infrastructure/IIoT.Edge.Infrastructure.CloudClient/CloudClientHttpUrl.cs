namespace IIoT.Edge.Infrastructure.CloudClient;

public static class CloudClientHttpUrl
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

    public static Uri Build(string baseUrl, string relativeOrAbsoluteUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        return Build(CreateHttpBaseUri(baseUrl), relativeOrAbsoluteUrl);
    }

    public static Uri Build(Uri baseUri, string relativeOrAbsoluteUrl)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeOrAbsoluteUrl);

        var value = relativeOrAbsoluteUrl.Trim();
        if (TryCreateAbsoluteHttpUri(value, out var absoluteUri))
        {
            return absoluteUri;
        }

        if (HasExplicitScheme(value))
        {
            throw new InvalidOperationException($"URL 只允许 HTTP/HTTPS 或相对 API path：{relativeOrAbsoluteUrl}");
        }

        return new Uri(baseUri, value.TrimStart('/'));
    }

    private static Uri CreateHttpBaseUri(string baseUrl)
    {
        if (Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var baseUri)
            && IsHttp(baseUri))
        {
            return baseUri;
        }

        throw new InvalidOperationException($"CloudApi:BaseUrl 无效: {baseUrl}");
    }

    private static bool HasExplicitScheme(string value)
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

    private static bool IsHttp(Uri uri)
        => string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
