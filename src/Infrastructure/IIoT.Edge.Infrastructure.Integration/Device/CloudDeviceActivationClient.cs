using System.Net.Http.Headers;
using IIoT.Edge.Infrastructure.CloudClient;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Module.Contracts.Device;

namespace IIoT.Edge.Infrastructure.Integration.Device;

public sealed record DeviceActivationReadyFacts(
    string GenerationId,
    string ClientCode,
    int Pid,
    string ModuleId,
    string PluginVersion,
    string PackageSha256,
    DateTimeOffset ReadyAtUtc);

public sealed record DeviceActivationResult(bool Success, string? ErrorCode = null)
{
    public static DeviceActivationResult Activated() => new(true);
    public static DeviceActivationResult Failed(string errorCode) => new(false, errorCode);
}

public interface IDeviceActivationCoordinator
{
    Task<DeviceActivationResult> EnsureActivatedAsync(
        DeviceActivationReadyFacts readyFacts,
        CancellationToken cancellationToken = default);
}

public interface ICloudDeviceActivationClient
{
    Task<CloudDeviceBootstrapResult> ActivateAsync(
        DeviceSession pendingSession,
        DeviceActivationReadyFacts readyFacts,
        CancellationToken cancellationToken);

    Task<bool> ConfirmAsync(
        DeviceSession activeSession,
        DeviceActivationReadyFacts readyFacts,
        CancellationToken cancellationToken);
}

internal sealed class CloudDeviceActivationClient : ICloudDeviceActivationClient
{
    private readonly ICloudClientHttpTransport _transport;
    private readonly ICloudApiEndpointProvider _endpointProvider;

    public CloudDeviceActivationClient(
        IHttpClientFactory httpClientFactory,
        ICloudApiEndpointProvider endpointProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _transport = new CloudClientHttpTransport(
            () => httpClientFactory.CreateClient(DeviceService.HttpClientName));
        _endpointProvider = endpointProvider ?? throw new ArgumentNullException(nameof(endpointProvider));
    }

    public async Task<CloudDeviceBootstrapResult> ActivateAsync(
        DeviceSession pendingSession,
        DeviceActivationReadyFacts readyFacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pendingSession);
        ArgumentNullException.ThrowIfNull(readyFacts);
        var token = pendingSession.ActivationAccessToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return CloudDeviceBootstrapResult.UnexpectedFailure(
                readyFacts.ClientCode,
                "Activation access token is missing.");
        }

        var body = new
        {
            generationId = readyFacts.GenerationId,
            clientCode = readyFacts.ClientCode,
            pid = readyFacts.Pid,
            moduleId = readyFacts.ModuleId,
            pluginVersion = readyFacts.PluginVersion,
            packageSha256 = readyFacts.PackageSha256,
            readyAtUtc = readyFacts.ReadyAtUtc
        };
        var result = await _transport.PostJsonAsync<object, EdgeCloudDeviceSessionDto>(
                new Uri(_endpointProvider.BuildUrl(_endpointProvider.GetDeviceActivatePath())),
                body,
                timeout: null,
                headers => headers.Authorization = new AuthenticationHeaderValue("Bearer", token),
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Success || result.Value is null)
        {
            return result.FailureKind switch
            {
                EdgeCloudFailureKind.HttpFailure => CloudDeviceBootstrapResult.HttpFailure(
                    readyFacts.ClientCode,
                    result.StatusCode ?? 0,
                    result.ErrorMessage),
                EdgeCloudFailureKind.Timeout => CloudDeviceBootstrapResult.Timeout(readyFacts.ClientCode),
                EdgeCloudFailureKind.NetworkFailure => CloudDeviceBootstrapResult.NetworkFailure(
                    readyFacts.ClientCode,
                    result.ErrorMessage ?? "Cloud activation network failure."),
                EdgeCloudFailureKind.Cancelled => CloudDeviceBootstrapResult.Cancelled(readyFacts.ClientCode),
                _ => CloudDeviceBootstrapResult.UnexpectedFailure(
                    readyFacts.ClientCode,
                    result.ErrorMessage ?? "Cloud activation failed.")
            };
        }

        var dto = result.Value;
        return CloudDeviceBootstrapResult.Success(readyFacts.ClientCode, new DeviceSession
        {
            SessionKind = string.IsNullOrWhiteSpace(dto.SessionKind) ? "Active" : dto.SessionKind.Trim(),
            GenerationId = dto.GenerationId,
            DeviceId = dto.Id,
            DeviceName = dto.DeviceName?.Trim() ?? string.Empty,
            ClientCode = string.IsNullOrWhiteSpace(dto.ClientCode)
                ? readyFacts.ClientCode
                : dto.ClientCode.Trim(),
            ProcessId = dto.ProcessId,
            UploadAccessToken = dto.UploadAccessToken,
            UploadAccessTokenExpiresAtUtc = dto.UploadAccessTokenExpiresAtUtc
                ?? CloudClientAuthHeaders.ReadAccessTokenExpiresAtUtc(result.ResponseHeaders),
            RefreshToken = dto.RefreshToken
                ?? CloudClientAuthHeaders.ReadRefreshToken(result.ResponseHeaders),
            RefreshTokenExpiresAtUtc = dto.RefreshTokenExpiresAtUtc
                ?? CloudClientAuthHeaders.ReadRefreshTokenExpiresAtUtc(result.ResponseHeaders)
        });
    }

    public async Task<bool> ConfirmAsync(
        DeviceSession activeSession,
        DeviceActivationReadyFacts readyFacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activeSession);
        ArgumentNullException.ThrowIfNull(readyFacts);
        var token = activeSession.UploadAccessToken?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var result = await _transport.PostJsonAsync(
                new Uri(_endpointProvider.BuildUrl(
                    _endpointProvider.GetDeviceActivateConfirmPath())),
                new
                {
                    generationId = readyFacts.GenerationId,
                    pid = readyFacts.Pid,
                    readyAtUtc = readyFacts.ReadyAtUtc
                },
                timeout: null,
                headers => headers.Authorization = new AuthenticationHeaderValue("Bearer", token),
                cancellationToken)
            .ConfigureAwait(false);
        return result.Success;
    }
}

public interface IDeviceActivationStateStore
{
    bool IsActivated(string clientCode, string generationId);

    void CommitActivating(DeviceSession activeSession, string generationId);

    void CommitActivated(DeviceSession activeSession, string generationId);
}
