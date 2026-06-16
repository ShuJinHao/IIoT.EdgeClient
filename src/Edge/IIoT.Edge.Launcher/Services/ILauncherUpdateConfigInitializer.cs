namespace IIoT.Edge.Launcher.Services;

public interface ILauncherUpdateConfigInitializer
{
    void EnsureConfigExists();

    bool TrySyncUpdateSource(string updateSource);
}
