namespace IIoT.Edge.Application.Auth.LocalAccounts;

public sealed class LocalAccountCatalogInitializer : ILocalAccountCatalogInitializer
{
    private readonly string _baseDirectory;

    public LocalAccountCatalogInitializer(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        _baseDirectory = baseDirectory;
    }

    public void EnsureCatalogExists()
    {
        var accountsPath = LocalAccountCatalog.GetCatalogPath(_baseDirectory);
        if (File.Exists(accountsPath))
        {
            return;
        }

        var samplePath = LocalAccountCatalog.GetCatalogPath(
            _baseDirectory,
            LocalAccountCatalog.SampleCatalogFileName);
        if (!File.Exists(samplePath))
        {
            throw new FileNotFoundException(
                $"本地账号文件不存在，且未找到样例文件：{samplePath}",
                samplePath);
        }

        File.Copy(samplePath, accountsPath, overwrite: false);
    }
}
