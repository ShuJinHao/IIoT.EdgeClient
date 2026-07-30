using IIoT.Edge.Host.DataPipeline.Context;
using IIoT.Edge.Testing;

namespace IIoT.Edge.Persistence.FilesystemTests;

public sealed class PlcIdentityAliasRegistryPersistenceTests
{
    [Fact]
    public void VerifiedAliases_ShouldSurviveRestartAndFailClosedWhenNameBelongsToMultipleCodes()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"iiot-edge-plc-alias-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var first = new PersistentPlcIdentityAliasRegistry(
                directory,
                new FakeLogService());
            first.ObserveVerifiedAlias("P1-AP01", "改名前");
            first.ObserveVerifiedAlias("P1-AP01", "改名后");

            var restored = new PersistentPlcIdentityAliasRegistry(
                directory,
                new FakeLogService());

            Assert.Equal(
                ["改名前", "改名后"],
                restored.GetVerifiedAliases("P1-AP01"));

            restored.ObserveVerifiedAlias("P1-AP02", "改名前");

            Assert.Equal(
                ["改名后"],
                restored.GetVerifiedAliases("P1-AP01"));
            Assert.Empty(restored.GetVerifiedAliases("P1-AP02"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StructurallyInvalidAliasFile_ShouldRemainUnusedAndNotBlockConstruction()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"iiot-edge-plc-alias-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "plc_identity_aliases.json");

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "P1-AP00": ["历史名称"],
                  "P1-AP01": null
                }
                """);
            var logger = new FakeLogService();

            var registry = new PersistentPlcIdentityAliasRegistry(directory, logger);

            Assert.Empty(registry.GetVerifiedAliases("P1-AP00"));
            Assert.Empty(registry.GetVerifiedAliases("P1-AP01"));
            Assert.True(File.Exists(path));
            Assert.Contains(
                logger.Entries,
                entry => entry.Level == "Warn"
                         && entry.Message.Contains("原文件已保留", StringComparison.Ordinal)
                         && entry.Message.Contains("不会用于归属", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
