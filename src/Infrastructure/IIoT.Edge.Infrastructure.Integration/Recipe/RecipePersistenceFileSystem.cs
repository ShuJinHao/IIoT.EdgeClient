namespace IIoT.Edge.Infrastructure.Integration.Recipe;

public sealed class RecipePersistenceException(string message, Exception innerException)
    : IOException(message, innerException);

internal interface IRecipePersistenceFileSystem
{
    bool FileExists(string path);

    string ReadAllText(string path);

    void WriteAllText(string path, string content);

    void ReplaceFile(string sourcePath, string destinationPath);

    void DeleteFile(string path);
}

internal sealed class RecipePersistenceFileSystem : IRecipePersistenceFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string content) => File.WriteAllText(path, content);

    public void ReplaceFile(string sourcePath, string destinationPath)
        => File.Move(sourcePath, destinationPath, overwrite: true);

    public void DeleteFile(string path) => File.Delete(path);
}
