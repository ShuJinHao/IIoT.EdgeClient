using IIoT.Edge.Infrastructure.Integration.Recipe;

namespace IIoT.Edge.Cloud.ContractFilesystemTests;

public sealed class RecipeServicePersistenceBehaviorTests
{
    [Fact]
    public void SetLocalParam_WhenAtomicReplaceFails_ShouldThrowAndKeepMemoryAndDiskUnchanged()
    {
        var recipeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"iiot-edge-recipe-persistence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(recipeDirectory);

        try
        {
            var fileSystem = new FaultingRecipePersistenceFileSystem();
            var logger = new FakeLogService();
            var service = CreateService(recipeDirectory, fileSystem, logger);
            var changedCount = 0;
            service.RecipeChanged += () => changedCount++;
            service.SwitchSource(IIoT.Edge.SharedKernel.DataPipeline.Recipe.RecipeSource.Local);
            changedCount = 0;
            service.SetLocalParam("Existing", 1, 2, "V");
            var persistedBefore = File.ReadAllText(Path.Combine(recipeDirectory, "local_recipe.json"));
            changedCount = 0;
            fileSystem.FailOnReplace = true;

            var exception = Assert.Throws<RecipePersistenceException>(() =>
                service.SetLocalParam("Rejected", 3, 4, "A"));

            Assert.Contains("原子保存", exception.Message, StringComparison.Ordinal);
            Assert.NotNull(service.LocalRecipe);
            Assert.Equal(["Existing"], service.LocalRecipe!.Parameters.Keys);
            Assert.Equal(persistedBefore, File.ReadAllText(Path.Combine(recipeDirectory, "local_recipe.json")));
            Assert.False(File.Exists(Path.Combine(recipeDirectory, "local_recipe.json.tmp")));
            Assert.Equal(0, changedCount);
            Assert.DoesNotContain(
                logger.Entries,
                entry => entry.Message.Contains("本地参数已更新：Rejected", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(recipeDirectory, recursive: true);
        }
    }

    [Fact]
    public void RemoveLocalParam_WhenAtomicReplaceFails_ShouldThrowAndKeepParameterVisible()
    {
        var recipeDirectory = Path.Combine(
            Path.GetTempPath(),
            $"iiot-edge-recipe-persistence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(recipeDirectory);

        try
        {
            var fileSystem = new FaultingRecipePersistenceFileSystem();
            var service = CreateService(recipeDirectory, fileSystem, new FakeLogService());
            service.SetLocalParam("Keep", 1, 2, "V");
            fileSystem.FailOnReplace = true;

            Assert.Throws<RecipePersistenceException>(() => service.RemoveLocalParam("Keep"));

            Assert.True(service.LocalRecipe!.Parameters.ContainsKey("Keep"));
            Assert.False(File.Exists(Path.Combine(recipeDirectory, "local_recipe.json.tmp")));
        }
        finally
        {
            Directory.Delete(recipeDirectory, recursive: true);
        }
    }

    private static RecipeService CreateService(
        string recipeDirectory,
        IRecipePersistenceFileSystem fileSystem,
        FakeLogService logger)
        => new(
            new FakeCloudHttpClient(),
            new FakeCloudApiEndpointProvider(),
            new FakeDeviceService(),
            logger,
            fileSystem,
            recipeDirectory);

    private sealed class FaultingRecipePersistenceFileSystem : IRecipePersistenceFileSystem
    {
        public bool FailOnReplace { get; set; }

        public bool FileExists(string path) => File.Exists(path);

        public string ReadAllText(string path) => File.ReadAllText(path);

        public void WriteAllText(string path, string content) => File.WriteAllText(path, content);

        public void ReplaceFile(string sourcePath, string destinationPath)
        {
            if (FailOnReplace)
                throw new IOException("replace failed");

            File.Move(sourcePath, destinationPath, overwrite: true);
        }

        public void DeleteFile(string path) => File.Delete(path);
    }
}
