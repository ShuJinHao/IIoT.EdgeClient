namespace IIoT.Edge.Infrastructure.Integration.Auth;

public sealed class CloudJwtValidationConfig
{
    public string JwtSigningKey { get; init; } = string.Empty;
    public string JwtIssuer { get; init; } = string.Empty;
    public string JwtAudience { get; init; } = string.Empty;
}
