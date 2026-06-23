namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherReleaseNotesDetailViewModel
{
    private const string EmptyText = "-";

    private LauncherReleaseNotesDetailViewModel(
        string displayName,
        string componentKindText,
        string currentVersion,
        string targetVersion,
        string statusKind,
        string statusText,
        string publishedAtText,
        string packageSizeText,
        string releaseNotesText)
    {
        DisplayName = Normalize(displayName);
        ComponentKindText = Normalize(componentKindText);
        CurrentVersion = Normalize(currentVersion);
        TargetVersion = Normalize(targetVersion);
        StatusKind = string.IsNullOrWhiteSpace(statusKind) ? "Neutral" : statusKind;
        StatusText = Normalize(statusText);
        PublishedAtText = Normalize(publishedAtText);
        PackageSizeText = Normalize(packageSizeText);
        ReleaseNotesText = Normalize(releaseNotesText);
    }

    public string DisplayName { get; }

    public string ComponentKindText { get; }

    public string CurrentVersion { get; }

    public string TargetVersion { get; }

    public string StatusKind { get; }

    public string StatusText { get; }

    public string PublishedAtText { get; }

    public string PackageSizeText { get; }

    public string ReleaseNotesText { get; }

    public static LauncherReleaseNotesDetailViewModel FromUpdateRow(LauncherClientPluginItem row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new LauncherReleaseNotesDetailViewModel(
            row.DisplayName,
            row.ComponentKindText,
            row.CurrentVersion,
            row.TargetVersion,
            row.StatusKind,
            row.StatusText,
            row.PublishedAtText,
            row.PackageSizeDisplayText,
            row.ReleaseNotesText);
    }

    public static LauncherReleaseNotesDetailViewModel FromVersionOption(
        LauncherVersionOptionItem option,
        string componentKindText)
    {
        ArgumentNullException.ThrowIfNull(option);

        return new LauncherReleaseNotesDetailViewModel(
            option.DisplayName,
            componentKindText,
            option.CurrentVersion,
            option.Version,
            option.StatusKind,
            option.StatusText,
            option.PublishedAtText,
            option.PackageSizeText,
            option.ReleaseNotes);
    }

    private static string Normalize(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) ? EmptyText : text;
    }
}
