using System.IO.Compression;
using System.Text;
using IIoT.Edge.Installer;
using IIoT.Edge.SharedKernel.Configuration;
using Xunit;

namespace IIoT.Edge.Installer.Tests;

public sealed class SelfExtractorTests
{
    [Fact]
    public void AppendThenReadAndExtract_ShouldRoundTripPayload()
    {
        var tempDir = CreateTempDir();
        try
        {
            // 1. 假"外壳"(模拟安装器 exe 字节)
            var stubPath = Path.Combine(tempDir, "stub.bin");
            File.WriteAllBytes(stubPath, Encoding.ASCII.GetBytes("FAKE-INSTALLER-STUB-BYTES"));

            // 2. 载荷 zip:launcher/iiot-binding.json + host + selected plugins
            byte[] payloadZip;
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteEntry(zip, "launcher/iiot-binding.json",
                        "{\"bindings\":[{\"moduleId\":\"Homogenization\",\"clientCode\":\"DEV-AAAAAAAAAA\"}]}");
                    WriteEntry(zip, "host/IIoT.Edge.Shell.dll", "shell-bytes");
                    WriteEntry(zip, "plugins/Homogenization/plugin.json", "{}");
                }
                payloadZip = ms.ToArray();
            }

            // 3. 合成成品:外壳 + 载荷 + 尾部
            var setupPath = Path.Combine(tempDir, "IIoT.Edge.Setup.exe");
            SelfExtractor.AppendPayload(stubPath, payloadZip, setupPath);

            // 4. 从成品读回载荷
            var readBack = SelfExtractor.ReadAppendedPayload(setupPath);
            Assert.NotNull(readBack);
            Assert.Equal(payloadZip, readBack);

            // 5. 解压并校验文件落位
            var installDir = Path.Combine(tempDir, "install");
            SelfExtractor.ExtractPayload(readBack!, installDir);

            var bindingPath = Path.Combine(installDir, "launcher", "iiot-binding.json");
            var shellPath = Path.Combine(installDir, "host", "IIoT.Edge.Shell.dll");
            var pluginPath = Path.Combine(installDir, "plugins", "Homogenization", "plugin.json");
            Assert.True(File.Exists(bindingPath));
            Assert.True(File.Exists(shellPath));
            Assert.True(File.Exists(pluginPath));
            Assert.Contains("DEV-AAAAAAAAAA", File.ReadAllText(bindingPath), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public void ReadAppendedPayload_ShouldReturnNullForPlainStub()
    {
        var tempDir = CreateTempDir();
        try
        {
            var stubPath = Path.Combine(tempDir, "stub.bin");
            File.WriteAllBytes(stubPath, Encoding.ASCII.GetBytes("PLAIN-STUB-NO-PAYLOAD"));

            Assert.Null(SelfExtractor.ReadAppendedPayload(stubPath));
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public void InstallerOptions_ShouldParseVelopackInstallDirectoryAndSilentMode()
    {
        var options = InstallerOptions.Parse([
            "--silent",
            "--installto",
            @"D:\IIoT\EdgeClient",
            "--no-launch"
        ]);

        Assert.Equal(@"D:\IIoT\EdgeClient", options.InstallTo);
        Assert.True(options.Silent);
        Assert.True(options.NoLaunch);
    }

    [Fact]
    public void VelopackPayload_ShouldCopyBootstrapBindingOutsideCurrent()
    {
        var tempDir = CreateTempDir();
        var previousDataRoot = Environment.GetEnvironmentVariable(
            EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                null);

            var payloadRoot = Path.Combine(tempDir, "payload");
            var payloadVelopackRoot = Path.Combine(payloadRoot, "velopack");
            var payloadLauncherRoot = Path.Combine(payloadRoot, "launcher");
            var payloadPluginRoot = Path.Combine(payloadRoot, "plugins", "Homogenization");
            Directory.CreateDirectory(payloadVelopackRoot);
            Directory.CreateDirectory(payloadLauncherRoot);
            Directory.CreateDirectory(payloadPluginRoot);
            var setupPath = Path.Combine(payloadVelopackRoot, "IIoT.EdgeClient-stable-Setup.exe");
            File.WriteAllText(setupPath, "fake setup");
            File.WriteAllText(
                Path.Combine(payloadLauncherRoot, "iiot-binding.json"),
                "{\"bindings\":[{\"moduleId\":\"Homogenization\",\"clientCode\":\"DEV-AAAAAAAAAA\"}]}");
            File.WriteAllText(
                Path.Combine(payloadLauncherRoot, "iiot-enabled-plugins.json"),
                "{\"plugins\":[{\"moduleId\":\"Homogenization\"}]}");
            File.WriteAllText(
                Path.Combine(payloadLauncherRoot, "launcher.update.json"),
                "{\"source\":\"https://cloud.example.com/edge-updates/velopack/stable\",\"channel\":\"stable\",\"targetRuntime\":\"win-x64\"}");
            File.WriteAllText(Path.Combine(payloadPluginRoot, "plugin.json"), "{}");

            var installRoot = Path.Combine(tempDir, "install");
            var currentRoot = Path.Combine(installRoot, "current");
            Directory.CreateDirectory(currentRoot);

            var discoveredSetup = SelfExtractor.FindVelopackSetup(payloadRoot);
            var setupArguments = SelfExtractor.BuildVelopackSetupArguments(installRoot, silent: true);
            SelfExtractor.CopyBootstrapFilesToVelopackDataRoot(payloadRoot, installRoot);
            var launcherDataRoot = EdgeClientProgramDataPaths.ResolveLauncherDirectory(currentRoot);

            Assert.Equal(setupPath, discoveredSetup);
            Assert.Equal(["--silent", "--installto", Path.GetFullPath(installRoot)], setupArguments);
            Assert.False(File.Exists(Path.Combine(currentRoot, "iiot-binding.json")));
            Assert.False(File.Exists(Path.Combine(currentRoot, "iiot-enabled-plugins.json")));
            Assert.False(File.Exists(Path.Combine(currentRoot, "launcher.update.json")));
            Assert.False(File.Exists(Path.Combine(currentRoot, "plugins", "Homogenization", "plugin.json")));
            Assert.True(File.Exists(Path.Combine(launcherDataRoot, "iiot-binding.json")));
            Assert.True(File.Exists(Path.Combine(launcherDataRoot, "iiot-enabled-plugins.json")));
            Assert.True(File.Exists(Path.Combine(launcherDataRoot, "launcher.update.json")));
            Assert.True(File.Exists(Path.Combine(installRoot, "plugins", "Homogenization", "plugin.json")));
            Assert.Contains(
                "DEV-AAAAAAAAAA",
                File.ReadAllText(Path.Combine(launcherDataRoot, "iiot-binding.json")),
                StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                previousDataRoot);
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public void VelopackPayload_WhenLauncherUpdateConfigMissing_ShouldFailFast()
    {
        var tempDir = CreateTempDir();
        try
        {
            var payloadRoot = Path.Combine(tempDir, "payload");
            var payloadLauncherRoot = Path.Combine(payloadRoot, "launcher");
            Directory.CreateDirectory(payloadLauncherRoot);
            File.WriteAllText(
                Path.Combine(payloadLauncherRoot, "iiot-binding.json"),
                "{\"bindings\":[{\"moduleId\":\"Homogenization\",\"clientCode\":\"DEV-AAAAAAAAAA\"}]}");
            File.WriteAllText(
                Path.Combine(payloadLauncherRoot, "iiot-enabled-plugins.json"),
                "{\"plugins\":[{\"moduleId\":\"Homogenization\"}]}");

            var installRoot = Path.Combine(tempDir, "install");
            Directory.CreateDirectory(Path.Combine(installRoot, "current"));

            var exception = Assert.Throws<FileNotFoundException>(() =>
                SelfExtractor.CopyBootstrapFilesToVelopackDataRoot(payloadRoot, installRoot));

            Assert.EndsWith(
                Path.Combine("launcher", "launcher.update.json"),
                exception.FileName,
                StringComparison.Ordinal);
        }
        finally
        {
            DeleteDir(tempDir);
        }
    }

    [Fact]
    public void BuildStartMenuShortcutPath_ShouldUseStableProductFolderAndShortcutName()
    {
        var path = InstallerService.BuildStartMenuShortcutPath(
            Path.Combine("C:", "Users", "operator", "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs"),
            InstallerService.DefaultShortcutName);

        Assert.EndsWith(
            Path.Combine("IIoT Edge", "IIoT Edge Client.lnk"),
            path,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VelopackPayload_ShouldRespectProgramDataRootOverrideWhenCopyingBootstrapBinding()
    {
        var tempDir = CreateTempDir();
        var previousDataRoot = Environment.GetEnvironmentVariable(
            EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable);
        try
        {
            var payloadRoot = Path.Combine(tempDir, "payload");
            var payloadLauncherRoot = Path.Combine(payloadRoot, "launcher");
            Directory.CreateDirectory(payloadLauncherRoot);
            File.WriteAllText(
                Path.Combine(payloadLauncherRoot, "iiot-binding.json"),
                "{\"bindings\":[{\"moduleId\":\"Homogenization\",\"clientCode\":\"DEV-AAAAAAAAAA\"}]}");
            File.WriteAllText(
                Path.Combine(payloadLauncherRoot, "iiot-enabled-plugins.json"),
                "{\"plugins\":[{\"moduleId\":\"Homogenization\"}]}");
            File.WriteAllText(
                Path.Combine(payloadLauncherRoot, "launcher.update.json"),
                "{\"source\":\"https://cloud.example.com/edge-updates/velopack/stable\",\"channel\":\"stable\",\"targetRuntime\":\"win-x64\"}");

            var dataRoot = Path.Combine(tempDir, "site-data");
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                dataRoot);

            var installRoot = Path.Combine(tempDir, "install");
            Directory.CreateDirectory(Path.Combine(installRoot, "current"));

            SelfExtractor.CopyBootstrapFilesToVelopackDataRoot(payloadRoot, installRoot);

            var launcherDataRoot = EdgeClientProgramDataPaths.ResolveLauncherDirectory(
                Path.Combine(installRoot, "current"));
            Assert.Equal(
                Path.Combine(dataRoot, "IIoT", "EdgeClient", "launcher"),
                launcherDataRoot);
            Assert.True(File.Exists(Path.Combine(launcherDataRoot, "iiot-binding.json")));
            Assert.True(File.Exists(Path.Combine(launcherDataRoot, "launcher.update.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable,
                previousDataRoot);
            DeleteDir(tempDir);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "iiot-installer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDir(string path)
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

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
