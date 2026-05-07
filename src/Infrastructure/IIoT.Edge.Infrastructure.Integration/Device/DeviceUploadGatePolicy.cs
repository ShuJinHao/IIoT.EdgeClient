using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Common.Device;

namespace IIoT.Edge.Infrastructure.Integration.Device;

public interface IDeviceUploadGatePolicy
{
    bool CanRefresh(DeviceSession? session);

    bool TryResolveTokenBlockReason(DeviceSession? session, out EdgeUploadBlockReason reason);

    EdgeUploadBlockReason ResolveBlockReason(DeviceSession? session, EdgeUploadBlockReason explicitReason);
}

public sealed class DeviceUploadGatePolicy : IDeviceUploadGatePolicy
{
    public bool CanRefresh(DeviceSession? session)
        => session is not null
            && !string.IsNullOrWhiteSpace(session.RefreshToken)
            && (!session.RefreshTokenExpiresAtUtc.HasValue || session.RefreshTokenExpiresAtUtc.Value > DateTimeOffset.UtcNow);

    public bool TryResolveTokenBlockReason(DeviceSession? session, out EdgeUploadBlockReason reason)
    {
        if (session is null || session.DeviceId == Guid.Empty)
        {
            reason = EdgeUploadBlockReason.DeviceUnidentified;
            return true;
        }

        if (string.IsNullOrWhiteSpace(session.UploadAccessToken))
        {
            reason = EdgeUploadBlockReason.MissingUploadToken;
            return true;
        }

        if (session.UploadAccessTokenExpiresAtUtc.HasValue
            && session.UploadAccessTokenExpiresAtUtc.Value <= DateTimeOffset.UtcNow)
        {
            reason = EdgeUploadBlockReason.ExpiredUploadToken;
            return true;
        }

        reason = EdgeUploadBlockReason.None;
        return false;
    }

    public EdgeUploadBlockReason ResolveBlockReason(DeviceSession? session, EdgeUploadBlockReason explicitReason)
    {
        if (explicitReason == EdgeUploadBlockReason.MissingUploadToken
            || explicitReason == EdgeUploadBlockReason.ExpiredUploadToken)
        {
            return explicitReason;
        }

        if (session is null)
        {
            return explicitReason == EdgeUploadBlockReason.None
                ? EdgeUploadBlockReason.DeviceUnidentified
                : explicitReason;
        }

        return explicitReason == EdgeUploadBlockReason.None
            ? ResolveFallbackTokenReason(session)
            : explicitReason;
    }

    private EdgeUploadBlockReason ResolveFallbackTokenReason(DeviceSession session)
        => TryResolveTokenBlockReason(session, out var tokenReason)
            ? tokenReason
            : EdgeUploadBlockReason.DeviceUnidentified;
}
