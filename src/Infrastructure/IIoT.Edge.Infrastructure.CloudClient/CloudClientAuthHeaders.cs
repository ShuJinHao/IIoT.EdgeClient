using System.Globalization;

namespace IIoT.Edge.Infrastructure.CloudClient;

public static class CloudClientAuthHeaders
{
    public const string RefreshToken = "X-IIoT-Refresh-Token";
    public const string BootstrapSecret = "X-IIoT-Bootstrap-Secret";
    public const string RefreshTokenExpiresAt = "X-IIoT-Refresh-Token-Expires-At";
    public const string AccessTokenExpiresAt = "X-IIoT-Access-Token-Expires-At";

    public static string? ReadRefreshToken(IReadOnlyDictionary<string, string> headers)
        => ReadSingle(headers, RefreshToken);

    public static DateTimeOffset? ReadRefreshTokenExpiresAtUtc(IReadOnlyDictionary<string, string> headers)
        => ReadTimestamp(headers, RefreshTokenExpiresAt);

    public static DateTimeOffset? ReadAccessTokenExpiresAtUtc(IReadOnlyDictionary<string, string> headers)
        => ReadTimestamp(headers, AccessTokenExpiresAt);

    private static string? ReadSingle(IReadOnlyDictionary<string, string> headers, string headerName)
        => headers.TryGetValue(headerName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static DateTimeOffset? ReadTimestamp(IReadOnlyDictionary<string, string> headers, string headerName)
    {
        var raw = ReadSingle(headers, headerName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }
}
