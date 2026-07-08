namespace IIoT.Edge.Launcher.Services;

public static class LauncherPasswordPolicy
{
    public const int MinimumLength = IIoT.Edge.SharedKernel.Security.EdgePasswordPolicy.MinimumLength;
    public const string RequirementMessage = IIoT.Edge.SharedKernel.Security.EdgePasswordPolicy.RequirementMessage;

    public static string? Validate(string? password)
        => IIoT.Edge.SharedKernel.Security.EdgePasswordPolicy.ValidateNewPassword(password);
}
