namespace IIoT.Edge.Application.Abstractions.Auth;

/// <summary>
/// Exposes the current in-memory cloud access token for integration clients.
/// </summary>
public interface ICloudAccessTokenProvider
{
    string? AccessToken { get; }
}
