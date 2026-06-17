namespace IIoT.Edge.Launcher.Services;

public sealed class LauncherAccountCatalogInitializer : ILauncherAccountCatalogInitializer
{
    private readonly LauncherAccountCatalogPaths _paths;

    public LauncherAccountCatalogInitializer(LauncherAccountCatalogPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.CatalogPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.SampleCatalogPath);

        _paths = paths;
    }

    public LauncherAccountCatalogInitializer(string baseDirectory)
        : this(new LauncherAccountCatalogPaths(
            LauncherAccountCatalog.GetCatalogPath(baseDirectory),
            LauncherAccountCatalog.GetCatalogPath(baseDirectory, LauncherAccountCatalog.SampleCatalogFileName)))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
    }

    public void EnsureCatalogExists()
    {
        if (File.Exists(_paths.CatalogPath) || !File.Exists(_paths.SampleCatalogPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_paths.CatalogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(_paths.SampleCatalogPath, _paths.CatalogPath, overwrite: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
