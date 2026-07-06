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
        // 缺账号文件必须进入首次配置/重置流程，不能静默复制 sample 账号。
        _ = _paths;
    }
}
