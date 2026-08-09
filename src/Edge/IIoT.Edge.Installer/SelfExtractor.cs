using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Installer;

/// <summary>
/// 自解压安装器的载荷读写：成品 .exe = [安装器外壳][载荷 zip][载荷长度 8 字节小端][magic 8 字节]。
/// 服务端按已绑定的设备插件选择，把【launcher + host + 选中的独立设备插件 + Binding】打成 zip 追加到外壳尾部，
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
    internal const string PayloadManifestFileName = "payload-manifest.json";

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
        var fullTarget = Path.GetFullPath(targetDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var extractedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                throw new InvalidDataException($"Installer payload contains an unsafe path: {entry.FullName}.");
            }

            if (!extractedPaths.Add(destination))
            {
                throw new InvalidDataException($"Installer payload contains a duplicate path: {entry.FullName}.");
            }

            var entryDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(entryDirectory))
            {
                Directory.CreateDirectory(entryDirectory);
            }

            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    /// <summary>
    /// Validates the Cloud-generated immutable payload manifest. The manifest covers every
    /// extracted file except itself; missing, extra, duplicate, size-mismatched or tampered
    /// bytes fail closed before Velopack or any credential write is attempted.
    /// </summary>
    public static InstallerPayloadManifest ValidatePayloadManifest(
        string payloadDirectory,
        IInstallerPayloadSignatureVerifier? signatureVerifier = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadDirectory);
        var root = Path.GetFullPath(payloadDirectory);
        var manifestPath = Path.Combine(root, PayloadManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                "Installer payload manifest is missing.",
                manifestPath);
        }

        var manifest = JsonSerializer.Deserialize<InstallerPayloadManifest>(
                File.ReadAllText(manifestPath),
                PayloadManifestJsonOptions)
            ?? throw new InvalidDataException("Installer payload manifest is empty.");
        if (manifest.SchemaVersion != 1
            || string.IsNullOrWhiteSpace(manifest.GenerationId)
            || string.IsNullOrWhiteSpace(manifest.Component)
            || string.IsNullOrWhiteSpace(manifest.Version)
            || manifest.CreatedAtUtc == default
            || manifest.Files is null
            || manifest.Files.Count == 0
            || manifest.Signature is null)
        {
            throw new InvalidDataException("Installer payload manifest header is invalid.");
        }

        var expected = new Dictionary<string, InstallerPayloadManifestFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            var relativePath = NormalizeManifestPath(file.Path);
            if (string.Equals(relativePath, PayloadManifestFileName, StringComparison.OrdinalIgnoreCase)
                || file.Size < 0
                || !IsSha256(file.Sha256)
                || !string.Equals(file.Sha256, file.Sha256.ToLowerInvariant(), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(file.Type)
                || string.IsNullOrWhiteSpace(file.Component)
                || string.IsNullOrWhiteSpace(file.Version)
                || !string.Equals(file.Path, relativePath, StringComparison.Ordinal)
                || !string.Equals(file.Type, file.Type.Trim(), StringComparison.Ordinal)
                || !string.Equals(file.Component, file.Component.Trim(), StringComparison.Ordinal)
                || !string.Equals(file.Version, file.Version.Trim(), StringComparison.Ordinal)
                || !expected.TryAdd(relativePath, file with { Path = relativePath }))
            {
                throw new InvalidDataException($"Installer payload manifest entry is invalid or duplicated: {file.Path}.");
            }
        }

        var normalizedManifest = manifest with
        {
            GenerationId = manifest.GenerationId,
            Component = manifest.Component,
            Version = manifest.Version,
            Files = expected.Values
                .OrderBy(static file => file.Path, StringComparer.Ordinal)
                .ToArray()
        };
        (signatureVerifier ?? RsaPssInstallerPayloadSignatureVerifier.CreateEmbedded())
            .Verify(normalizedManifest, CreateCanonicalManifestBytes(normalizedManifest));

        var actual = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                FullPath = path,
                RelativePath = NormalizeManifestPath(Path.GetRelativePath(root, path))
            })
            .Where(static file => !string.Equals(
                file.RelativePath,
                PayloadManifestFileName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var actualPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in actual)
        {
            if (!actualPaths.Add(file.RelativePath)
                || !expected.Remove(file.RelativePath, out var declared))
            {
                throw new InvalidDataException($"Installer payload contains an undeclared or duplicate file: {file.RelativePath}.");
            }

            var info = new FileInfo(file.FullPath);
            if (info.Length != declared.Size)
            {
                throw new InvalidDataException($"Installer payload file size does not match manifest: {file.RelativePath}.");
            }

            using var stream = File.OpenRead(file.FullPath);
            var actualSha256 = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actualSha256, declared.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Installer payload file hash does not match manifest: {file.RelativePath}.");
            }
        }

        if (expected.Count != 0)
        {
            throw new InvalidDataException(
                $"Installer payload is incomplete; manifest file is missing: {expected.Keys.Order(StringComparer.Ordinal).First()}.");
        }

        return normalizedManifest;
    }

    /// <summary>在 payload 解压目录中定位 Velopack Setup.exe；找不到则返回 null，调用方应停止安装。</summary>
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
        throw new NotSupportedException(
            "Copying raw iiot-binding.json is prohibited. Use InstallerPayloadTransaction so credentials are imported and runtime Binding is sanitized atomically.");
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

    private static string NormalizeManifestPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
        {
            throw new InvalidDataException("Installer payload manifest path is empty or invalid.");
        }

        var normalized = value.Replace('\\', '/').TrimStart('/');
        if (Path.IsPathRooted(value)
            || normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"Installer payload manifest path is unsafe: {value}.");
        }

        return normalized;
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    internal static byte[] CreateCanonicalManifestBytes(InstallerPayloadManifest manifest)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", manifest.SchemaVersion);
            writer.WriteString("generationId", manifest.GenerationId);
            writer.WriteString("component", manifest.Component);
            writer.WriteString("version", manifest.Version);
            writer.WriteString(
                "createdAtUtc",
                manifest.CreatedAtUtc.UtcDateTime.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                    System.Globalization.CultureInfo.InvariantCulture));
            writer.WritePropertyName("files");
            writer.WriteStartArray();
            foreach (var file in manifest.Files.OrderBy(static file => file.Path, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", NormalizeManifestPath(file.Path));
                writer.WriteNumber("size", file.Size);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteString("type", file.Type);
                writer.WriteString("component", file.Component);
                writer.WriteString("version", file.Version);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private static readonly JsonSerializerOptions PayloadManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

internal sealed record InstallerPayloadManifest(
    int SchemaVersion,
    string GenerationId,
    string Component,
    string Version,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<InstallerPayloadManifestFile> Files,
    InstallerPayloadSignature Signature);

internal sealed record InstallerPayloadManifestFile(
    string Path,
    long Size,
    string Sha256,
    string Type,
    string Component,
    string Version);

internal sealed record InstallerPayloadSignature(
    string Algorithm,
    string? KeyId,
    string? Value);

internal interface IInstallerPayloadSignatureVerifier
{
    void Verify(InstallerPayloadManifest manifest, ReadOnlySpan<byte> canonicalManifest);
}

internal sealed class RsaPssInstallerPayloadSignatureVerifier(
    IReadOnlyDictionary<string, string> trustedPublicKeys) : IInstallerPayloadSignatureVerifier
{
    private const string SupportedAlgorithm = "rsa-pss-sha256";
    private const string EmbeddedTrustResourceSuffix = "trusted-payload-signing-keys.json";

    public void Verify(InstallerPayloadManifest manifest, ReadOnlySpan<byte> canonicalManifest)
    {
        if (!string.Equals(
                manifest.Signature.Algorithm,
                SupportedAlgorithm,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Installer payload signature algorithm '{manifest.Signature.Algorithm}' is not trusted.");
        }

        var keyId = manifest.Signature.KeyId?.Trim();
        if (string.IsNullOrWhiteSpace(keyId)
            || !trustedPublicKeys.TryGetValue(keyId, out var publicKeyPem)
            || string.IsNullOrWhiteSpace(publicKeyPem))
        {
            throw new InvalidDataException(
                $"Installer payload signing key '{keyId ?? "<missing>"}' is not trusted by this installer.");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(manifest.Signature.Value ?? string.Empty);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Installer payload signature is not valid Base64.", ex);
        }

        if (signature.Length == 0)
        {
            throw new InvalidDataException("Installer payload signature is empty.");
        }

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(publicKeyPem);
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new InvalidDataException($"Trusted installer key '{keyId}' is invalid.", ex);
        }

        if (!rsa.VerifyData(
                canonicalManifest,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss))
        {
            throw new InvalidDataException("Installer payload signature verification failed.");
        }
    }

    public static RsaPssInstallerPayloadSignatureVerifier CreateEmbedded()
    {
        var assembly = typeof(RsaPssInstallerPayloadSignatureVerifier).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(
                EmbeddedTrustResourceSuffix,
                StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new InvalidDataException(
                "Installer contains no embedded trusted payload signing keys.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException("Installer trusted signing-key resource cannot be read.");
        var document = JsonSerializer.Deserialize<TrustedPayloadKeysDocument>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Installer trusted signing-key resource is empty.");
        var keys = (document.Keys ?? [])
            .Where(static key => !string.IsNullOrWhiteSpace(key.KeyId)
                                 && !string.IsNullOrWhiteSpace(key.PublicKeyPem))
            .ToDictionary(
                static key => key.KeyId.Trim(),
                static key => key.PublicKeyPem,
                StringComparer.Ordinal);
        if (keys.Count == 0)
        {
            throw new InvalidDataException(
                "Installer contains no provisioned trusted payload signing key; installation is blocked.");
        }

        return new RsaPssInstallerPayloadSignatureVerifier(keys);
    }

    private sealed record TrustedPayloadKeysDocument(IReadOnlyList<TrustedPayloadKey>? Keys);

    private sealed record TrustedPayloadKey(string KeyId, string PublicKeyPem);
}
