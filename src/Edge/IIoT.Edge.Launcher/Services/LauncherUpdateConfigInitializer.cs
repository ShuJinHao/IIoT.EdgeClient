namespace IIoT.Edge.Launcher.Services;

public sealed record LauncherUpdateConfigPaths(string ConfigPath, string SampleConfigPath);

public sealed class LauncherUpdateConfigInitializer : ILauncherUpdateConfigInitializer
{
    public const string SampleConfigFileName = "launcher.update.sample.json";

    private readonly LauncherUpdateConfigPaths _paths;

    public LauncherUpdateConfigInitializer(LauncherUpdateConfigPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.ConfigPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.SampleConfigPath);

        _paths = paths;
    }

    public void EnsureConfigExists()
    {
        if (File.Exists(_paths.ConfigPath) || !File.Exists(_paths.SampleConfigPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_paths.ConfigPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(_paths.SampleConfigPath, _paths.ConfigPath, overwrite: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
