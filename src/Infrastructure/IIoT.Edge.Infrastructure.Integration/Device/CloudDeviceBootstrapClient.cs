using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.CloudClient;

namespace IIoT.Edge.Infrastructure.Integration.Device;

public interface ICloudDeviceBootstrapClient
{
    Task<CloudDeviceBootstrapResult> BootstrapAsync(CancellationToken ct);

    Task<CloudDeviceBootstrapResult> RefreshAsync(DeviceSession session, CancellationToken ct);
}

public sealed class CloudDeviceBootstrapClient : ICloudDeviceBootstrapClient
{
    private readonly IEdgeCloudDeviceBootstrapClient _cloudClient;
    private readonly ICloudApiEndpointProvider _endpointProvider;

    public CloudDeviceBootstrapClient(
        IHttpClientFactory httpClientFactory,
        ICloudApiEndpointProvider endpointProvider)
        : this(
            new EdgeCloudDeviceBootstrapClient(
                new CloudClientHttpTransport(() => httpClientFactory.CreateClient(DeviceService.HttpClientName))),
            endpointProvider)
    {
    }

    internal CloudDeviceBootstrapClient(
        IEdgeCloudDeviceBootstrapClient cloudClient,
        ICloudApiEndpointProvider endpointProvider)
    {
        _cloudClient = cloudClient ?? throw new ArgumentNullException(nameof(cloudClient));
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

            var result = await _cloudClient
                .BootstrapAsync(
                    new EdgeCloudDeviceBootstrapRequest(clientCode, bootstrapSecret, new Uri(url)),
                    ct)
                .ConfigureAwait(false);
            return MapResult(result);
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

            var result = await _cloudClient
                .RefreshAsync(
                    new EdgeCloudDeviceRefreshRequest(
                        clientCode,
                        session.RefreshToken,
                        new Uri(_endpointProvider.BuildUrl(_endpointProvider.GetBootstrapRefreshPath()))),
                    ct)
                .ConfigureAwait(false);
            return MapResult(result);
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

    private static CloudDeviceBootstrapResult MapResult(EdgeCloudDeviceBootstrapResult result)
        => result.Kind switch
        {
            EdgeCloudDeviceBootstrapResultKind.Success when result.Session is not null
                => CloudDeviceBootstrapResult.Success(result.ClientCode, new DeviceSession
                {
                    DeviceId = result.Session.DeviceId,
                    DeviceName = result.Session.DeviceName,
                    ClientCode = result.Session.ClientCode,
                    ProcessId = result.Session.ProcessId,
                    UploadAccessToken = result.Session.UploadAccessToken,
                    UploadAccessTokenExpiresAtUtc = result.Session.UploadAccessTokenExpiresAtUtc,
                    RefreshToken = result.Session.RefreshToken,
                    RefreshTokenExpiresAtUtc = result.Session.RefreshTokenExpiresAtUtc
                }),
            EdgeCloudDeviceBootstrapResultKind.HttpFailure
                => CloudDeviceBootstrapResult.HttpFailure(
                    result.ClientCode,
                    result.StatusCode ?? 0,
                    result.ErrorMessage),
            EdgeCloudDeviceBootstrapResultKind.EmptyPayload
                => CloudDeviceBootstrapResult.EmptyPayload(result.ClientCode),
            EdgeCloudDeviceBootstrapResultKind.Timeout
                => CloudDeviceBootstrapResult.Timeout(result.ClientCode),
            EdgeCloudDeviceBootstrapResultKind.NetworkFailure
                => CloudDeviceBootstrapResult.NetworkFailure(
                    result.ClientCode,
                    result.ErrorMessage ?? "Cloud 网络请求失败。"),
            EdgeCloudDeviceBootstrapResultKind.Cancelled
                => CloudDeviceBootstrapResult.Cancelled(result.ClientCode),
            _ => CloudDeviceBootstrapResult.UnexpectedFailure(
                result.ClientCode,
                result.ErrorMessage ?? "Cloud bootstrap 失败。")
        };
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
