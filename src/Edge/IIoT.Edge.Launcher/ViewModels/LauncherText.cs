using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Launcher.ViewModels;

internal static class LauncherText
{
    public static string Get(IAppLanguageService? languageService, string key)
        => languageService?.GetString(key, FallbackFor(key)) ?? StaticText(key, FallbackFor(key));

    public static string Format(IAppLanguageService? languageService, string key, params object[] args)
        => languageService?.Format(key, FallbackFor(key), args)
            ?? string.Format(
                global::System.Globalization.CultureInfo.CurrentCulture,
                StaticText(key, FallbackFor(key)),
                args);

    public static string Compact(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        var normalized = detail
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return normalized.Length <= 180
            ? normalized
            : normalized[..180] + "...";
    }

    public static string FallbackFor(string key) => key switch
    {
        "Launcher_Welcome_Format" => "{0}",
        "Launcher_ProfileSummary_AllFormat" => "{0}",
        "Launcher_ProfileSummary_FilteredFormat" => "{0} / {1}",
        "Launcher_Status_LaunchSucceededFormat" => "{0} {1}",
        "Launcher_Status_LaunchFailedFormat" => "{0}",
        "Launcher_Update_StatusNoUpdate" => "{0}",
        "Launcher_Update_StatusAvailable" => "{0}",
        "Launcher_Update_StatusPendingRestart" => "{0}",
        "Launcher_Update_ShellRunningDetail" => string.Empty,
        "Launcher_ClientRelease_StatusChecking" => "{0}",
        "Launcher_ClientRelease_StatusInstalling" => "{0}",
        "Launcher_ClientRelease_StatusApplyingVersion" => "{0} {1}",
        "Launcher_ClientRelease_StatusHostApplyStarted" => "{0}",
        "Launcher_ClientRelease_StatusInstalled" => "{0}",
        "Launcher_ClientRelease_StatusReady" => "{0} {1} {2}",
        "Launcher_VersionManagement_ConfirmRollbackMessage" => "{0} {1} {2}",
        "Launcher_VersionManagement_ConfirmDeprecatedMessage" => "{0} {1} {2}",
        "Launcher_ClientRelease_ShellRunningDetail" => string.Empty,
        "Launcher_ClientRelease_Plugin_NotInstalled" => "-",
        _ => key
    };

    private static string StaticText(string key, string fallback)
    {
        var app = global::Avalonia.Application.Current;
        return app?.TryGetResource(key, null, out var value) == true
            && value is string text
            && !string.IsNullOrWhiteSpace(text)
                ? text
                : fallback;
    }
}
