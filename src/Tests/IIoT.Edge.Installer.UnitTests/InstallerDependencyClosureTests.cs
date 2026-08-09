using System.Text.Json;

namespace IIoT.Edge.Installer.UnitTests;

public sealed class InstallerDependencyClosureTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "edge-installer-closure-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ValidateDepsClosure_WhenManagedNativeAndResourceUseExactPublishPaths_ShouldPass()
    {
        WriteFixture();

        InstallerPayloadTransaction.ValidateDepsClosure(_root, "Fixture.deps.json");
    }

    [Fact]
    public void ValidateDepsClosure_WhenSameNamedManagedFileExistsOnlyInWrongDirectory_ShouldFail()
    {
        WriteFixture();
        File.Move(
            Path.Combine(_root, "Managed.Dependency.dll"),
            CreateFile("decoy/Managed.Dependency.dll", "managed"),
            overwrite: true);

        var exception = Assert.Throws<FileNotFoundException>(() =>
            InstallerPayloadTransaction.ValidateDepsClosure(_root, "Fixture.deps.json"));

        Assert.Contains("exact publish path", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateDepsClosure_WhenNativeAssetIsDeleted_ShouldFail()
    {
        WriteFixture();
        File.Delete(Path.Combine(_root, "native-runtime.dll"));

        Assert.Throws<FileNotFoundException>(() =>
            InstallerPayloadTransaction.ValidateDepsClosure(_root, "Fixture.deps.json"));
    }

    [Fact]
    public void ValidateDepsClosure_WhenLocalizedResourceIsMovedToRoot_ShouldFail()
    {
        WriteFixture();
        File.Move(
            Path.Combine(_root, "zh-Hans", "Runtime.resources.dll"),
            Path.Combine(_root, "Runtime.resources.dll"));

        Assert.Throws<FileNotFoundException>(() =>
            InstallerPayloadTransaction.ValidateDepsClosure(_root, "Fixture.deps.json"));
    }

    private void WriteFixture()
    {
        Directory.CreateDirectory(_root);
        CreateFile("Fixture.dll", "entry");
        CreateFile("Managed.Dependency.dll", "managed");
        CreateFile("native-runtime.dll", "native");
        CreateFile("zh-Hans/Runtime.resources.dll", "resource");
        var deps = new
        {
            targets = new Dictionary<string, object>
            {
                ["fixture/win-x64"] = new Dictionary<string, object>
                {
                    ["Fixture/1.0.0"] = new
                    {
                        runtime = new Dictionary<string, object>
                        {
                            ["Fixture.dll"] = new { },
                            ["lib/net10.0/Managed.Dependency.dll"] = new { }
                        },
                        native = new Dictionary<string, object>
                        {
                            ["runtimes/win-x64/native/native-runtime.dll"] = new { }
                        },
                        resources = new Dictionary<string, object>
                        {
                            ["lib/net10.0/Runtime.resources.dll"] = new { locale = "zh-Hans" }
                        }
                    }
                }
            }
        };
        File.WriteAllText(
            Path.Combine(_root, "Fixture.deps.json"),
            JsonSerializer.Serialize(deps));
    }

    private string CreateFile(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
