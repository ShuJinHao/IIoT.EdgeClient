namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherLoginSucceededEventArgs : EventArgs
{
    public LauncherLoginSucceededEventArgs(string displayName)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : displayName.Trim();
    }

    public string DisplayName { get; }
}
