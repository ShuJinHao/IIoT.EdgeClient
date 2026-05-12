using System.IO;

namespace IIoT.Edge.Launcher.Services;

public sealed class LauncherAccountCatalogInitializer : ILauncherAccountCatalogInitializer
{
    private readonly string _baseDirectory;

    public LauncherAccountCatalogInitializer(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        _baseDirectory = baseDirectory;
    }

    public void EnsureCatalogExists()
    {
        var accountsPath = LauncherAccountCatalog.GetCatalogPath(_baseDirectory);
        if (File.Exists(accountsPath))
        {
            return;
        }

        var samplePath = LauncherAccountCatalog.GetCatalogPath(
            _baseDirectory,
            LauncherAccountCatalog.SampleCatalogFileName);
        if (!File.Exists(samplePath))
        {
            throw new FileNotFoundException(
                $"启动账号文件不存在，且未找到样例文件：{samplePath}",
                samplePath);
        }

        File.Copy(samplePath, accountsPath, overwrite: false);
    }
}
