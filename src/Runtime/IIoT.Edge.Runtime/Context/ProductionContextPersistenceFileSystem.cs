using System.Text;

namespace IIoT.Edge.Runtime.Context;

internal interface IProductionContextPersistenceFileSystem
{
    void WriteAllText(string path, string content);

    void ReplaceFile(string sourcePath, string destinationPath);

    bool FileExists(string path);

    void DeleteFile(string path);
}

internal sealed class ProductionContextPersistenceFileSystem : IProductionContextPersistenceFileSystem
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public void WriteAllText(string path, string content)
        => File.WriteAllText(path, content, Utf8NoBom);

    public void ReplaceFile(string sourcePath, string destinationPath)
        => File.Move(sourcePath, destinationPath, overwrite: true);

    public bool FileExists(string path) => File.Exists(path);

    public void DeleteFile(string path) => File.Delete(path);
}
