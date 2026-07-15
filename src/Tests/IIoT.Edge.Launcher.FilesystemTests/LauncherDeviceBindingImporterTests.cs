using IIoT.Edge.Infrastructure.Update.Profiles;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.SharedKernel.Configuration;
using Xunit;

namespace IIoT.Edge.Launcher.FilesystemTests;

public sealed class LauncherDeviceBindingImporterTests
{
    [Fact]
    public void ApplyPendingBindings_ShouldReadPendingBindingFromProgramDataLauncherDirectory()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var currentDirectory = Path.Combine(tempDirectory, "install", "current");
            var hostDirectory = Path.Combine(currentDirectory, "host");
            Directory.CreateDirectory(hostDirectory);

            WriteText(
                Path.Combine(hostDirectory, "appsettings.machine.LineA.json"),
                """
                {
                  "CloudApi": { "ClientCode": "", "BootstrapSecret": "" },
                  "Modules": { "Enabled": [ "TestPlugin" ] }
                }
                """);

            WithDataRoot(dataRoot, () =>
            {
                var launcherDir = EdgeClientProgramDataPaths.ResolveLauncherDirectory(currentDirectory);
                var pendingPath = Path.Combine(launcherDir, LauncherDeviceBindingImporter.BindingFileName);
                WriteText(
                    pendingPath,
                    """
                    {
                      "schemaVersion": 1,
                      "baseUrl": "http://cloud.local:81",
                      "bindings": [
                        {
                          "moduleId": "TestPlugin",
                          "clientCode": "DEV-AAAAAAAAAA",
                          "bootstrapSecret": "SEC-HOMOG-001"
                        }
                      ]
                    }
                    """);

                var importer = new LauncherDeviceBindingImporter(
                    currentDirectory,
                    new FakeProfileCatalog(Profile(hostDirectory)),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory());

                importer.ApplyPendingBindings();

                var externalConfig = File.ReadAllText(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("LineA", hostDirectory));
                Assert.Contains("\"ClientCode\": \"DEV-AAAAAAAAAA\"", externalConfig, StringComparison.Ordinal);
                Assert.Contains("\"BootstrapSecret\": \"SEC-HOMOG-001\"", externalConfig, StringComparison.Ordinal);
                Assert.False(File.Exists(pendingPath));

                var appliedFiles = Directory.GetFiles(launcherDir, "iiot-binding.applied.*.json");
                Assert.Single(appliedFiles);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ApplyPendingBindings_ShouldIgnoreBaseDirectoryBinding()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);

            // 打包机器配置：声明启用模块 TestPlugin（moduleId -> profile 据此匹配）
            WriteText(
                Path.Combine(hostDirectory, "appsettings.machine.LineA.json"),
                """
                {
                  "CloudApi": { "ClientCode": "", "BootstrapSecret": "" },
                  "Modules": { "Enabled": [ "TestPlugin" ] }
                }
                """);

            // 旧 exe 目录绑定文件不再属于当前安装契约。
            var legacyBindingPath = Path.Combine(hostDirectory, LauncherDeviceBindingImporter.BindingFileName);
            WriteText(
                legacyBindingPath,
                """
                {
                  "schemaVersion": 1,
                  "baseUrl": "http://cloud.local:81",
                  "bindings": [
                    {
                      "moduleId": "TestPlugin",
                      "clientCode": "DEV-AAAAAAAAAA",
                      "bootstrapSecret": "SEC-HOMOG-001",
                      "deviceName": "测试插件线1#"
                    }
                  ]
                }
                """);

            WithDataRoot(dataRoot, () =>
            {
                var importer = new LauncherDeviceBindingImporter(
                    hostDirectory,
                    new FakeProfileCatalog(Profile(hostDirectory)),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory());

                importer.ApplyPendingBindings();

                Assert.False(File.Exists(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("LineA", hostDirectory)));
                Assert.True(File.Exists(legacyBindingPath));

                var launcherDir = EdgeClientProgramDataPaths.ResolveLauncherDirectory(hostDirectory);
                Assert.False(Directory.Exists(launcherDir));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ApplyPendingBindings_ShouldBeNoOpWhenBindingFileMissing()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            WriteText(
                Path.Combine(hostDirectory, "appsettings.machine.LineA.json"),
                """
                { "Modules": { "Enabled": [ "TestPlugin" ] } }
                """);

            WithDataRoot(dataRoot, () =>
            {
                var importer = new LauncherDeviceBindingImporter(
                    hostDirectory,
                    new FakeProfileCatalog(Profile(hostDirectory)),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory());

                // 无绑定文件：不抛异常、也不创建外部配置
                var exception = Record.Exception(() => importer.ApplyPendingBindings());

                Assert.Null(exception);
                Assert.False(File.Exists(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath("LineA", hostDirectory)));
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    [Fact]
    public void ApplyPendingBindings_ShouldNotThrowOnCorruptBindingFile()
    {
        var tempDirectory = CreateTempDirectory();
        var dataRoot = Path.Combine(tempDirectory, "program-data");
        try
        {
            var hostDirectory = Path.Combine(tempDirectory, "host");
            Directory.CreateDirectory(hostDirectory);
            WriteText(
                Path.Combine(hostDirectory, LauncherDeviceBindingImporter.BindingFileName),
                "{ not valid json");

            WithDataRoot(dataRoot, () =>
            {
                var importer = new LauncherDeviceBindingImporter(
                    hostDirectory,
                    new FakeProfileCatalog(Profile(hostDirectory)),
                    new FileEdgeProfileModuleConfigurationStore(),
                    new LauncherUpdateTargetFactory());

                // 启动红线：JSON 损坏不得抛 fatal
                var exception = Record.Exception(() => importer.ApplyPendingBindings());

                Assert.Null(exception);
            });
        }
        finally
        {
            DeleteDirectory(tempDirectory);
        }
    }

    private sealed class FakeProfileCatalog : ILauncherProfileCatalog
    {
        private readonly IReadOnlyList<LauncherProfileDefinition> _profiles;

        public FakeProfileCatalog(params LauncherProfileDefinition[] profiles) => _profiles = profiles;

        public IReadOnlyList<LauncherProfileDefinition> LoadProfiles() => _profiles;
    }

    private static LauncherProfileDefinition Profile(string hostDirectory)
        => new(
            "LineA",
            "Line A",
            "测试 profile",
            null,
            "LineA",
            Path.Combine(hostDirectory, "IIoT.Edge.Shell"),
            "BeakerOutline",
            "#4D7C0F");

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "iiot-edge-binding-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }

    private static void WithDataRoot(string dataRoot, Action action)
    {
        var previous = Environment.GetEnvironmentVariable(
            EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        Environment.SetEnvironmentVariable(
            EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, dataRoot);
        try
        {
            action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable, previous);
        }
    }
}
