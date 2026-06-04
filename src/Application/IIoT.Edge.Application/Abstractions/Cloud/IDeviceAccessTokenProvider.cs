namespace IIoT.Edge.Application.Abstractions.Cloud;

public interface IDeviceAccessTokenProvider
{
    string? AccessToken { get; }

    DateTimeOffset? AccessTokenExpiresAtUtc { get; }
}
