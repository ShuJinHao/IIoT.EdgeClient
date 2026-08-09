using IIoT.Edge.Infrastructure.HostPersistence;

namespace IIoT.Edge.Launcher.FilesystemTests;

public sealed class LauncherHostDatabaseRecoveryTests
{
    [Fact]
    public void FirstInitialization_ShouldCreateUsableIndependentRecoveryAndRestoreCorruptLiveFile()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "iiot-launcher-host-db-recovery-tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "host.db");
        var recoveryPath = databasePath + ".recovery";

        try
        {
            var database = new LauncherHostDatabase(databasePath, legacyAccountCatalogPath: null);

            database.EnsureCreatedAndMigrate();

            Assert.True(File.Exists(databasePath));
            Assert.True(File.Exists(recoveryPath));
            Assert.True(new FileInfo(databasePath).Length > 0);
            Assert.True(new FileInfo(recoveryPath).Length > 0);

            File.WriteAllBytes(databasePath, [0x49, 0x49, 0x4f, 0x54]);

            database.EnsureCreatedAndMigrate();

            Assert.Empty(database.LoadAccounts());
            Assert.Contains(
                Directory.EnumerateFiles(root, "host.db.corrupt.*"),
                path => new FileInfo(path).Length == 4);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
