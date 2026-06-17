using System.Buffers.Binary;
using System.IO.Compression;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Installer;

/// <summary>
/// 自解压安装器的载荷读写：成品 .exe = [安装器外壳][载荷 zip][载荷长度 8 字节小端][magic 8 字节]。
/// 服务端按你勾选的工序，把【launcher + host + 选中的 plugins + 绑定 JSON】打成 zip 追加到外壳尾部，
/// 得到一个双击即装的 .exe;安装器运行时从自身尾部读回 zip 解压。整条链不需要在服务端编译。
/// </summary>
internal static class SelfExtractor
{
    private static readonly byte[] Magic = "IIOTEDG1"u8.ToArray();
    private const int TrailerLength = 16; // 8(长度) + 8(magic)
    private const string VelopackPayloadDirectoryName = "velopack";
    private const string BindingFileName = "iiot-binding.json";
    private const string EnabledPluginsFileName = "iiot-enabled-plugins.json";
    private const string UpdateConfigFileName = "launcher.update.json";
    private const string PluginsRootDirectoryName = "plugins";

    /// <summary>读取自身 exe 尾部追加的载荷(zip 字节);没有则返回 null。</summary>
    public static byte[]? ReadAppendedPayload(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        if (stream.Length < TrailerLength)
        {
            return null;
        }

        var trailer = new byte[TrailerLength];
        stream.Seek(-TrailerLength, SeekOrigin.End);
        ReadExact(stream, trailer, TrailerLength);

        if (!trailer.AsSpan(8, 8).SequenceEqual(Magic))
        {
            return null;
        }

        var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(trailer.AsSpan(0, 8));
        if (payloadLength <= 0 || payloadLength > stream.Length - TrailerLength)
        {
            return null;
        }

        var payload = new byte[payloadLength];
        stream.Seek(-(TrailerLength + payloadLength), SeekOrigin.End);
        ReadExact(stream, payload, (int)payloadLength);
        return payload;
    }

    /// <summary>把载荷 zip 解压到目标目录(带 zip 路径穿越防护)。</summary>
    public static void ExtractPayload(byte[] payloadZip, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        var fullTarget = Path.GetFullPath(targetDirectory);

        using var zipStream = new MemoryStream(payloadZip, writable: false);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue; // 目录项
            }

            var destination = Path.GetFullPath(Path.Combine(fullTarget, entry.FullName));
            if (!destination.StartsWith(fullTarget + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue; // 防 zip 穿越
            }

            var entryDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(entryDirectory))
            {
                Directory.CreateDirectory(entryDirectory);
            }

            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    /// <summary>在 payload 解压目录中定位 Velopack Setup.exe；找不到则返回 null，调用方回退旧解压安装。</summary>
    public static string? FindVelopackSetup(string payloadDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadDirectory);

        var candidates = new List<string>();
        var velopackDirectory = Path.Combine(payloadDirectory, VelopackPayloadDirectoryName);
        if (Directory.Exists(velopackDirectory))
        {
            candidates.AddRange(Directory.EnumerateFiles(
                velopackDirectory,
                "*Setup.exe",
                SearchOption.AllDirectories));
        }

        if (Directory.Exists(payloadDirectory))
        {
            candidates.AddRange(Directory.EnumerateFiles(
                payloadDirectory,
                "*Setup.exe",
                SearchOption.TopDirectoryOnly));
        }

        return candidates
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                "IIoT.Edge.Setup.exe",
                StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static string GetDefaultInstallRoot()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IIoTEdge");

    public static string ResolveInstallRoot(string? requestedInstallRoot)
    {
        var root = string.IsNullOrWhiteSpace(requestedInstallRoot)
            ? GetDefaultInstallRoot()
            : Environment.ExpandEnvironmentVariables(requestedInstallRoot.Trim());

        return Path.GetFullPath(root);
    }

    public static string GetVelopackCurrentDirectory(string installRoot)
        => Path.Combine(ResolveInstallRoot(installRoot), "current");

    public static string[] BuildVelopackSetupArguments(string installRoot, bool silent)
    {
        var arguments = new List<string>();
        if (silent)
        {
            arguments.Add("--silent");
        }

        arguments.Add("--installto");
        arguments.Add(ResolveInstallRoot(installRoot));
        return arguments.ToArray();
    }

    public static void CopyBootstrapFilesToVelopackDataRoot(string payloadDirectory, string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadDirectory);
        var currentDirectory = GetVelopackCurrentDirectory(installRoot);
        var launcherDirectory = EdgeClientProgramDataPaths.ResolveLauncherDirectory(currentDirectory);

        CopyRequiredFile(
            Path.Combine(payloadDirectory, "launcher", BindingFileName),
            Path.Combine(launcherDirectory, BindingFileName));
        CopyRequiredFile(
            Path.Combine(payloadDirectory, "launcher", EnabledPluginsFileName),
            Path.Combine(launcherDirectory, EnabledPluginsFileName));
        CopyIfExists(
            Path.Combine(payloadDirectory, "launcher", UpdateConfigFileName),
            Path.Combine(launcherDirectory, UpdateConfigFileName));

        CopyDirectoryContentsIfExists(
            Path.Combine(payloadDirectory, PluginsRootDirectoryName),
            Path.Combine(ResolveInstallRoot(installRoot), PluginsRootDirectoryName));
    }

    /// <summary>生成成品 .exe:外壳 + 载荷 + 尾部。供发布脚本/服务端打包与测试使用。</summary>
    public static void AppendPayload(string stubPath, byte[] payloadZip, string outputPath)
    {
        File.Copy(stubPath, outputPath, overwrite: true);

        using var output = new FileStream(outputPath, FileMode.Append, FileAccess.Write);
        output.Write(payloadZip);

        Span<byte> trailer = stackalloc byte[TrailerLength];
        BinaryPrimitives.WriteInt64LittleEndian(trailer[..8], payloadZip.Length);
        Magic.CopyTo(trailer[8..]);
        output.Write(trailer);
    }

    /// <summary>
    /// 回退安装路径专用：直接解压到 installRoot 后，把引导文件从
    /// {installRoot}/launcher/ 拷贝到 ProgramData 路径（{layoutRoot}/data/IIoT/EdgeClient/launcher/），
    /// 保证 Launcher 在 ProgramData 路径能找到 launcher.update.json 等配置。
    /// </summary>
    public static void CopyBootstrapFilesToFallbackDataRoot(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);

        var launcherSourceDirectory = Path.Combine(installRoot, "launcher");
        var launcherTargetDirectory = EdgeClientProgramDataPaths.ResolveLauncherDirectory(
            Path.Combine(installRoot, "launcher"));

        if (string.Equals(
                Path.GetFullPath(launcherSourceDirectory),
                Path.GetFullPath(launcherTargetDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CopyRequiredFile(
            Path.Combine(launcherSourceDirectory, BindingFileName),
            Path.Combine(launcherTargetDirectory, BindingFileName));
        CopyRequiredFile(
            Path.Combine(launcherSourceDirectory, EnabledPluginsFileName),
            Path.Combine(launcherTargetDirectory, EnabledPluginsFileName));
        CopyIfExists(
            Path.Combine(launcherSourceDirectory, UpdateConfigFileName),
            Path.Combine(launcherTargetDirectory, UpdateConfigFileName));
    }

    private static void CopyIfExists(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static void CopyRequiredFile(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Required bootstrap file was not found in installer payload.", sourcePath);
        }

        CopyIfExists(sourcePath, targetPath);
    }

    private static void CopyDirectoryContentsIfExists(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetPath = Path.Combine(targetDirectory, relativePath);
            var targetFileDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetFileDirectory))
            {
                Directory.CreateDirectory(targetFileDirectory);
            }

            File.Copy(sourceFile, targetPath, overwrite: true);
        }
    }

    private static void ReadExact(Stream stream, byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }
            offset += read;
        }
    }
}
