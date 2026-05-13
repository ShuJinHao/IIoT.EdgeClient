using System.Net.Http.Json;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Http;

namespace IIoT.Edge.Infrastructure.Integration.Device;

public interface ICloudDeviceBootstrapClient
{
    Task<CloudDeviceBootstrapResult> BootstrapAsync(CancellationToken ct);

    Task<CloudDeviceBootstrapResult> RefreshAsync(DeviceSession session, CancellationToken ct);
}

public sealed class CloudDeviceBootstrapClient : ICloudDeviceBootstrapClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICloudApiEndpointProvider _endpointProvider;

    public CloudDeviceBootstrapClient(
        IHttpClientFactory httpClientFactory,
        ICloudApiEndpointProvider endpointProvider)
    {
        _httpClientFactory = httpClientFactory;
        _endpointProvider = endpointProvider;
    }

    public async Task<CloudDeviceBootstrapResult> BootstrapAsync(CancellationToken ct)
    {
        var clientCode = string.Empty;
        try
        {
            clientCode = _endpointProvider.GetClientCode();
            var bootstrapSecret = _endpointProvider.GetBootstrapSecret();
            var deviceInstancePath = _endpointProvider.GetDeviceInstancePath();
            var url = _endpointProvider.BuildUrl(
                $"{deviceInstancePath}?clientCode={Uri.EscapeDataString(clientCode)}");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation(CloudAuthHeaders.BootstrapSecret, bootstrapSecret);

            using var response = await CreateHttpClient().SendAsync(request, ct).ConfigureAwait(false);
            return await ReadBootstrapResponseAsync(response, clientCode, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return CloudDeviceBootstrapResult.Cancelled(clientCode);
        }
        catch (TaskCanceledException)
        {
            return CloudDeviceBootstrapResult.Timeout(clientCode);
        }
        catch (HttpRequestException ex)
        {
            return CloudDeviceBootstrapResult.NetworkFailure(clientCode, ex.Message);
        }
        catch (Exception ex)
        {
            return CloudDeviceBootstrapResult.UnexpectedFailure(clientCode, ex.Message);
        }
    }

    public async Task<CloudDeviceBootstrapResult> RefreshAsync(DeviceSession session, CancellationToken ct)
    {
        var clientCode = string.IsNullOrWhiteSpace(session.ClientCode)
            ? string.Empty
            : session.ClientCode;
        try
        {
            clientCode = string.IsNullOrWhiteSpace(session.ClientCode)
                ? _endpointProvider.GetClientCode()
                : session.ClientCode;

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                _endpointProvider.BuildUrl(_endpointProvider.GetBootstrapRefreshPath()));
            request.Headers.TryAddWithoutValidation(CloudAuthHeaders.RefreshToken, session.RefreshToken);

            using var response = await CreateHttpClient().SendAsync(request, ct).ConfigureAwait(false);
            return await ReadBootstrapResponseAsync(response, clientCode, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return CloudDeviceBootstrapResult.Cancelled(clientCode);
        }
        catch (TaskCanceledException)
        {
            return CloudDeviceBootstrapResult.Timeout(clientCode);
        }
        catch (HttpRequestException ex)
        {
            return CloudDeviceBootstrapResult.NetworkFailure(clientCode, ex.Message);
        }
        catch (Exception ex)
        {
            return CloudDeviceBootstrapResult.UnexpectedFailure(clientCode, ex.Message);
        }
    }

    private async Task<CloudDeviceBootstrapResult> ReadBootstrapResponseAsync(
        HttpResponseMessage response,
        string clientCode,
        CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = await TryReadFirstErrorAsync(response, ct).ConfigureAwait(false);
            return CloudDeviceBootstrapResult.HttpFailure(clientCode, (int)response.StatusCode, errorMessage);
        }

        var dto = await response.Content.ReadFromJsonAsync<DeviceResponseDto>(ct).ConfigureAwait(false);
        if (dto is null)
        {
            return CloudDeviceBootstrapResult.EmptyPayload(clientCode);
        }

        dto.RefreshToken ??= CloudAuthHeaders.ReadRefreshToken(response);
        dto.RefreshTokenExpiresAtUtc ??= CloudAuthHeaders.ReadRefreshTokenExpiresAtUtc(response);
        dto.UploadAccessTokenExpiresAtUtc ??= CloudAuthHeaders.ReadAccessTokenExpiresAtUtc(response);

        return CloudDeviceBootstrapResult.Success(clientCode, new DeviceSession
        {
            DeviceId = dto.Id,
            DeviceName = dto.DeviceName,
            ClientCode = string.IsNullOrWhiteSpace(dto.ClientCode) ? clientCode : dto.ClientCode,
            ProcessId = dto.ProcessId,
            UploadAccessToken = dto.UploadAccessToken,
            UploadAccessTokenExpiresAtUtc = dto.UploadAccessTokenExpiresAtUtc,
            RefreshToken = dto.RefreshToken,
            RefreshTokenExpiresAtUtc = dto.RefreshTokenExpiresAtUtc
        });
    }

    private static async Task<string?> TryReadFirstErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ApiErrorEnvelope>(ct).ConfigureAwait(false);
            return envelope?.Errors?.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private HttpClient CreateHttpClient()
        => _httpClientFactory.CreateClient(DeviceService.HttpClientName);
}

public sealed record CloudDeviceBootstrapResult(
    CloudDeviceBootstrapResultKind Kind,
    string ClientCode,
    DeviceSession? Session = null,
    int? StatusCode = null,
    string? ErrorMessage = null)
{
    public static CloudDeviceBootstrapResult Success(string clientCode, DeviceSession session)
        => new(CloudDeviceBootstrapResultKind.Success, clientCode, session);

    public static CloudDeviceBootstrapResult HttpFailure(string clientCode, int statusCode, string? errorMessage)
        => new(CloudDeviceBootstrapResultKind.HttpFailure, clientCode, StatusCode: statusCode, ErrorMessage: errorMessage);

    public static CloudDeviceBootstrapResult EmptyPayload(string clientCode)
        => new(CloudDeviceBootstrapResultKind.EmptyPayload, clientCode);

    public static CloudDeviceBootstrapResult Timeout(string clientCode)
        => new(CloudDeviceBootstrapResultKind.Timeout, clientCode);

    public static CloudDeviceBootstrapResult NetworkFailure(string clientCode, string errorMessage)
        => new(CloudDeviceBootstrapResultKind.NetworkFailure, clientCode, ErrorMessage: errorMessage);

    public static CloudDeviceBootstrapResult UnexpectedFailure(string clientCode, string errorMessage)
        => new(CloudDeviceBootstrapResultKind.UnexpectedFailure, clientCode, ErrorMessage: errorMessage);

    public static CloudDeviceBootstrapResult Cancelled(string clientCode)
        => new(CloudDeviceBootstrapResultKind.Cancelled, clientCode);
}

public enum CloudDeviceBootstrapResultKind
{
    Success,
    HttpFailure,
    EmptyPayload,
    Timeout,
    NetworkFailure,
    UnexpectedFailure,
    Cancelled
}
