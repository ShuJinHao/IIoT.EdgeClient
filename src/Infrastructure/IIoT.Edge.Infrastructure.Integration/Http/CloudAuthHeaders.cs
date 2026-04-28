using System.Globalization;

namespace IIoT.Edge.Infrastructure.Integration.Http;

public static class CloudAuthHeaders
{
    public const string RefreshToken = "X-IIoT-Refresh-Token";
    public const string RefreshTokenExpiresAt = "X-IIoT-Refresh-Token-Expires-At";
    public const string AccessTokenExpiresAt = "X-IIoT-Access-Token-Expires-At";

    public static string? ReadRefreshToken(HttpResponseMessage response)
        => ReadSingle(response, RefreshToken);

    public static DateTimeOffset? ReadRefreshTokenExpiresAtUtc(HttpResponseMessage response)
        => ReadTimestamp(response, RefreshTokenExpiresAt);

    public static DateTimeOffset? ReadAccessTokenExpiresAtUtc(HttpResponseMessage response)
        => ReadTimestamp(response, AccessTokenExpiresAt);

    private static string? ReadSingle(HttpResponseMessage response, string headerName)
    {
        return response.Headers.TryGetValues(headerName, out var values)
            ? values.FirstOrDefault()?.Trim()
            : null;
    }

    private static DateTimeOffset? ReadTimestamp(HttpResponseMessage response, string headerName)
    {
        var raw = ReadSingle(response, headerName);
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
