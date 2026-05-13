namespace IIoT.Edge.Application.Abstractions.Device;

public interface IDeviceAccessTokenProvider
{
    string? AccessToken { get; }

    DateTimeOffset? AccessTokenExpiresAtUtc { get; }
}
