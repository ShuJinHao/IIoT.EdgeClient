namespace IIoT.Edge.Infrastructure.CloudClient;

public interface IEdgeCloudDeviceBootstrapClient
{
    Task<EdgeCloudDeviceBootstrapResult> BootstrapAsync(
        EdgeCloudDeviceBootstrapRequest request,
        CancellationToken cancellationToken = default);

    Task<EdgeCloudDeviceBootstrapResult> RefreshAsync(
        EdgeCloudDeviceRefreshRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class EdgeCloudDeviceBootstrapClient : IEdgeCloudDeviceBootstrapClient
{
    private readonly ICloudClientHttpTransport _transport;

    public EdgeCloudDeviceBootstrapClient(ICloudClientHttpTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<EdgeCloudDeviceBootstrapResult> BootstrapAsync(
        EdgeCloudDeviceBootstrapRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await _transport
            .GetJsonAsync<EdgeCloudDeviceSessionDto>(
                request.Url,
                request.Timeout,
                headers => headers.TryAddWithoutValidation(CloudClientAuthHeaders.BootstrapSecret, request.BootstrapSecret),
                cancellationToken)
            .ConfigureAwait(false);
        return Map(request.ClientCode, response);
    }

    public async Task<EdgeCloudDeviceBootstrapResult> RefreshAsync(
        EdgeCloudDeviceRefreshRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await _transport
            .PostJsonAsync<object, EdgeCloudDeviceSessionDto>(
                request.Url,
                null,
                request.Timeout,
                headers => headers.TryAddWithoutValidation(CloudClientAuthHeaders.RefreshToken, request.RefreshToken),
                cancellationToken)
            .ConfigureAwait(false);
        return Map(request.ClientCode, response);
    }

    private static EdgeCloudDeviceBootstrapResult Map(
        string clientCode,
        EdgeCloudOperationResult<EdgeCloudDeviceSessionDto> response)
    {
        if (!response.Success)
        {
            return response.FailureKind switch
            {
                EdgeCloudFailureKind.HttpFailure => EdgeCloudDeviceBootstrapResult.HttpFailure(
                    clientCode,
                    response.StatusCode ?? 0,
                    response.ErrorMessage),
                EdgeCloudFailureKind.EmptyPayload => EdgeCloudDeviceBootstrapResult.EmptyPayload(clientCode),
                EdgeCloudFailureKind.Timeout => EdgeCloudDeviceBootstrapResult.Timeout(clientCode),
                EdgeCloudFailureKind.NetworkFailure => EdgeCloudDeviceBootstrapResult.NetworkFailure(
                    clientCode,
                    response.ErrorMessage ?? "Cloud 网络请求失败。"),
                EdgeCloudFailureKind.Cancelled => EdgeCloudDeviceBootstrapResult.Cancelled(clientCode),
                _ => EdgeCloudDeviceBootstrapResult.UnexpectedFailure(
                    clientCode,
                    response.ErrorMessage ?? "Cloud 请求失败。")
            };
        }

        var dto = response.Value;
        if (dto is null)
        {
            return EdgeCloudDeviceBootstrapResult.EmptyPayload(clientCode);
        }

        var session = new EdgeCloudDeviceSession(
            dto.Id,
            dto.DeviceName ?? string.Empty,
            string.IsNullOrWhiteSpace(dto.ClientCode) ? clientCode : dto.ClientCode!,
            dto.ProcessId,
            dto.UploadAccessToken,
            dto.UploadAccessTokenExpiresAtUtc ?? CloudClientAuthHeaders.ReadAccessTokenExpiresAtUtc(response.ResponseHeaders),
            dto.RefreshToken ?? CloudClientAuthHeaders.ReadRefreshToken(response.ResponseHeaders),
            dto.RefreshTokenExpiresAtUtc ?? CloudClientAuthHeaders.ReadRefreshTokenExpiresAtUtc(response.ResponseHeaders));

        return EdgeCloudDeviceBootstrapResult.Success(clientCode, session);
    }
}

public sealed record EdgeCloudDeviceBootstrapRequest(
    string ClientCode,
    string BootstrapSecret,
    Uri Url,
    TimeSpan? Timeout = null);

public sealed record EdgeCloudDeviceRefreshRequest(
    string ClientCode,
    string? RefreshToken,
    Uri Url,
    TimeSpan? Timeout = null);

public sealed record EdgeCloudDeviceSession(
    Guid DeviceId,
    string DeviceName,
    string ClientCode,
    Guid ProcessId,
    string? UploadAccessToken,
    DateTimeOffset? UploadAccessTokenExpiresAtUtc,
    string? RefreshToken,
    DateTimeOffset? RefreshTokenExpiresAtUtc);

public sealed record EdgeCloudDeviceBootstrapResult(
    EdgeCloudDeviceBootstrapResultKind Kind,
    string ClientCode,
    EdgeCloudDeviceSession? Session = null,
    int? StatusCode = null,
    string? ErrorMessage = null)
{
    public static EdgeCloudDeviceBootstrapResult Success(string clientCode, EdgeCloudDeviceSession session)
        => new(EdgeCloudDeviceBootstrapResultKind.Success, clientCode, session);

    public static EdgeCloudDeviceBootstrapResult HttpFailure(string clientCode, int statusCode, string? errorMessage)
        => new(EdgeCloudDeviceBootstrapResultKind.HttpFailure, clientCode, StatusCode: statusCode, ErrorMessage: errorMessage);

    public static EdgeCloudDeviceBootstrapResult EmptyPayload(string clientCode)
        => new(EdgeCloudDeviceBootstrapResultKind.EmptyPayload, clientCode);

    public static EdgeCloudDeviceBootstrapResult Timeout(string clientCode)
        => new(EdgeCloudDeviceBootstrapResultKind.Timeout, clientCode);

    public static EdgeCloudDeviceBootstrapResult NetworkFailure(string clientCode, string errorMessage)
        => new(EdgeCloudDeviceBootstrapResultKind.NetworkFailure, clientCode, ErrorMessage: errorMessage);

    public static EdgeCloudDeviceBootstrapResult UnexpectedFailure(string clientCode, string errorMessage)
        => new(EdgeCloudDeviceBootstrapResultKind.UnexpectedFailure, clientCode, ErrorMessage: errorMessage);

    public static EdgeCloudDeviceBootstrapResult Cancelled(string clientCode)
        => new(EdgeCloudDeviceBootstrapResultKind.Cancelled, clientCode);
}

public enum EdgeCloudDeviceBootstrapResultKind
{
    Success,
    HttpFailure,
    EmptyPayload,
    Timeout,
    NetworkFailure,
    UnexpectedFailure,
    Cancelled
}

public sealed class EdgeCloudDeviceSessionDto
{
    public Guid Id { get; set; }
    public string? DeviceName { get; set; }
    public string? ClientCode { get; set; }
    public Guid ProcessId { get; set; }
    public string? UploadAccessToken { get; set; }
    public DateTimeOffset? UploadAccessTokenExpiresAtUtc { get; set; }
    public string? RefreshToken { get; set; }
    public DateTimeOffset? RefreshTokenExpiresAtUtc { get; set; }
}
