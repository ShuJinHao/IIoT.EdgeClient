namespace IIoT.Edge.Application.Abstractions.Auth;

public enum LocalAdminCredentialStatus
{
    NotConfigured,
    Ready,
    RequiresPasswordReset,
    Invalid
}
