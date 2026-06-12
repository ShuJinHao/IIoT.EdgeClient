using System.IO.Compression;
using System.Text;
using IIoT.Edge.Installer;
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
