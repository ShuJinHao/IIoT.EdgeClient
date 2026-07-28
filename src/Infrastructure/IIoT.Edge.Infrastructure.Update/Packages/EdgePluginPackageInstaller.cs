using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Infrastructure.Update.Cloud;
using IIoT.Edge.Infrastructure.Update.Plugins;
using IIoT.Edge.SharedKernel.Configuration;
using static IIoT.Edge.Infrastructure.Update.Cloud.EdgeUpdateCloudUrl;

namespace IIoT.Edge.Infrastructure.Update.Packages;

public sealed class EdgePluginPackageInstaller
{
    private readonly HttpClient _httpClient;
    private readonly IEdgeVersionCompatibilityPolicy _compatibilityPolicy;
    private readonly EdgePluginPackageInstallLimits _limits;

    public EdgePluginPackageInstaller(IEdgeVersionCompatibilityPolicy compatibilityPolicy)
        : this(
            new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            compatibilityPolicy)
    {
    }

    public EdgePluginPackageInstaller(
        HttpClient httpClient,
        IEdgeVersionCompatibilityPolicy compatibilityPolicy,
        EdgePluginPackageInstallLimits? limits = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _compatibilityPolicy = compatibilityPolicy ?? throw new ArgumentNullException(nameof(compatibilityPolicy));
        _limits = limits ?? EdgePluginPackageInstallLimits.Default;
    }

    internal async Task<PreparedEdgePluginPackage> PrepareAsync(
        string stagingRoot,
        EdgePluginVersionRelease release,
        EdgeUpdateCloudApiOptions cloudOptions,
        string hostVersion,
        string hostApiVersion,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (!_compatibilityPolicy.IsReleaseCompatible(release, hostVersion, hostApiVersion, out var issue))
        {
            throw new InvalidOperationException(issue);
        }

        var packagePath = Path.Combine(stagingRoot, "package.zip");
        var extractDirectory = Path.Combine(stagingRoot, "extract");
        Directory.CreateDirectory(stagingRoot);
        progress?.Report(1);
        await DownloadPackageAsync(
            release.DownloadUrl,
            cloudOptions.BaseUrl,
            packagePath,
            _limits,
            progress,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePackageFileSize(packagePath, _limits);

        var actualSha256 = await ComputeSha256Async(packagePath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actualSha256, release.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"插件包 SHA256 不匹配: {release.ModuleId} {release.PackageVersion}");
        }

        progress?.Report(70);
        ValidateZipEntries(packagePath, extractDirectory, _limits);
        ZipFile.ExtractToDirectory(packagePath, extractDirectory);
        var manifest = ValidateExtractedPackage(
            extractDirectory,
            release,
            hostVersion,
            hostApiVersion);
        var activationProfiles = ValidateActivationIfPresent(
            extractDirectory,
            release.ModuleId);
        WriteInstallRecord(extractDirectory, release, manifest, actualSha256);
        return new PreparedEdgePluginPackage(
            release.ModuleId,
            release.PackageVersion,
            actualSha256,
            stagingRoot,
            extractDirectory,
            activationProfiles);
    }

    private async Task DownloadPackageAsync(
        string packageUrl,
        string baseUrl,
        string packagePath,
        EdgePluginPackageInstallLimits limits,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var source = ResolvePackageUri(packageUrl, baseUrl);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(limits.DownloadTimeout);
        using var response = await _httpClient
            .GetAsync(source, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        if (total is > 0 && total.Value > limits.MaxPackageBytes)
        {
            throw new InvalidOperationException($"插件包大小超过限制: {total.Value} bytes");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = File.Create(packagePath);
        var buffer = new byte[64 * 1024];
        long readTotal = 0;
        int read;
        while ((read = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readTotal += read;
            if (readTotal > limits.MaxPackageBytes)
            {
                throw new InvalidOperationException($"插件包大小超过限制: {readTotal} bytes");
            }

            if (total is > 0)
            {
                progress?.Report(Math.Clamp((int)(readTotal * 60 / total.Value), 1, 60));
            }
        }

        progress?.Report(60);
    }

    private static Uri ResolvePackageUri(string packageUrl, string baseUrl)
    {
        var candidate = packageUrl?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new InvalidOperationException("插件包地址为空。");
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
        {
            return IsHttpSource(absolute)
                ? absolute
                : throw new InvalidOperationException(
                    "插件包地址只允许绝对 HTTP/HTTPS URL 或 catalog 相对 URL。");
        }

        if (candidate.StartsWith("/", StringComparison.Ordinal)
            || candidate.StartsWith("\\", StringComparison.Ordinal)
            || candidate.Contains('\\')
            || !Uri.TryCreate(candidate, UriKind.Relative, out _))
        {
            throw new InvalidOperationException(
                "插件包地址只允许绝对 HTTP/HTTPS URL 或 catalog 相对 URL。");
        }

        var resolved = BuildUrl(baseUrl, candidate);
        return IsHttpSource(resolved)
            ? resolved
            : throw new InvalidOperationException(
                "插件包地址只允许绝对 HTTP/HTTPS URL 或 catalog 相对 URL。");
    }

    private static bool IsHttpSource(Uri source)
        => source.Scheme == Uri.UriSchemeHttp
           || source.Scheme == Uri.UriSchemeHttps;

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void ValidatePackageFileSize(string packagePath, EdgePluginPackageInstallLimits limits)
    {
        var packageSize = new FileInfo(packagePath).Length;
        if (packageSize > limits.MaxPackageBytes)
        {
            throw new InvalidOperationException($"插件包大小超过限制: {packageSize} bytes");
        }
    }

    private static void ValidateZipEntries(
        string packagePath,
        string extractDirectory,
        EdgePluginPackageInstallLimits limits)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        long totalUncompressedBytes = 0;
        var fileCount = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            _ = ResolveSafePackagePath(extractDirectory, entry.FullName, $"插件包包含非法路径: {entry.FullName}");
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            fileCount++;
            if (fileCount > limits.MaxFileCount)
            {
                throw new InvalidOperationException($"插件包文件数量超过限制: {fileCount}");
            }

            if (entry.Length > limits.MaxEntryBytes)
            {
                throw new InvalidOperationException($"插件包单文件大小超过限制: {entry.FullName}");
            }

            totalUncompressedBytes += entry.Length;
            if (totalUncompressedBytes > limits.MaxExtractedBytes)
            {
                throw new InvalidOperationException($"插件包解压后大小超过限制: {totalUncompressedBytes} bytes");
            }

            if (entry.CompressedLength == 0 && entry.Length > 0)
            {
                throw new InvalidOperationException($"插件包条目压缩大小异常: {entry.FullName}");
            }

            if (entry.CompressedLength > 0
                && entry.Length / (double)entry.CompressedLength > limits.MaxCompressionRatio)
            {
                throw new InvalidOperationException($"插件包压缩比超过限制: {entry.FullName}");
            }
        }
    }

    private EdgePluginManifest ValidateExtractedPackage(
        string extractDirectory,
        EdgePluginVersionRelease release,
        string hostVersion,
        string hostApiVersion)
    {
        var manifestPath = Path.Combine(extractDirectory, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("插件包根目录缺少 plugin.json。");
        }

        var manifest = JsonSerializer.Deserialize<EdgePluginManifest>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("plugin.json 无法解析。");
        if (!string.Equals(manifest.ModuleId, release.ModuleId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.Version, release.PackageVersion, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.HostApiVersion, release.HostApiVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("plugin.json 与 catalog 发布记录不一致。");
        }

        var entryAssembly = ResolveSafePackagePath(
            extractDirectory,
            manifest.EntryAssembly,
            $"插件入口程序集路径非法: {manifest.EntryAssembly}");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) || !File.Exists(entryAssembly))
        {
            throw new InvalidOperationException($"插件入口程序集不存在: {manifest.EntryAssembly}");
        }

        if (!_compatibilityPolicy.IsReleaseCompatible(release, hostVersion, hostApiVersion, out var issue))
        {
            throw new InvalidOperationException(issue);
        }

        return manifest;
    }

    private static IReadOnlyList<PreparedEdgePluginActivationProfile>
        ValidateActivationIfPresent(
        string extractDirectory,
        string moduleId)
    {
        var activationRoot = Path.Combine(extractDirectory, "activation");
        if (!Directory.Exists(activationRoot))
        {
            return [];
        }

        var manifestPath = Path.Combine(activationRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("插件 activation 目录缺少 manifest.json。");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        if (!TryGetProperty(root, "schemaVersion", out var schemaVersion)
            || !schemaVersion.TryGetInt32(out var parsedSchemaVersion)
            || parsedSchemaVersion != 1
            || !TryGetProperty(root, "moduleId", out var activationModuleId)
            || activationModuleId.ValueKind != JsonValueKind.String
            || !string.Equals(
                activationModuleId.GetString(),
                moduleId,
                StringComparison.OrdinalIgnoreCase)
            || !TryGetProperty(root, "profiles", out var profiles)
            || profiles.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("插件 activation manifest 无效。");
        }

        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var activationProfiles =
            new List<PreparedEdgePluginActivationProfile>();
        foreach (var profile in profiles.EnumerateArray())
        {
            var profileId = ReadRequiredString(profile, "profileId");
            if (!profileIds.Add(profileId))
            {
                throw new InvalidOperationException(
                    $"插件 activation profileId 重复: {profileId}。");
            }

            var launcherProfile = ReadRequiredString(profile, "launcherProfile");
            var machineConfig = ReadRequiredString(profile, "machineConfig");
            var launcherProfilePath = ResolveSafePackagePath(
                activationRoot,
                launcherProfile,
                $"activation launcherProfile 路径非法: {launcherProfile}");
            var machineConfigPath = ResolveSafePackagePath(
                activationRoot,
                machineConfig,
                $"activation machineConfig 路径非法: {machineConfig}");
            if (!File.Exists(launcherProfilePath) || !File.Exists(machineConfigPath))
            {
                throw new InvalidOperationException("插件 activation 引用文件不存在。");
            }

            ValidateActivationProfile(
                launcherProfilePath,
                machineConfigPath,
                moduleId,
                profileId);
            activationProfiles.Add(
                new PreparedEdgePluginActivationProfile(
                    profileId,
                    Path.GetRelativePath(
                        extractDirectory,
                        machineConfigPath)));
        }

        return activationProfiles;
    }

    private static void ValidateActivationProfile(
        string launcherProfilePath,
        string machineConfigPath,
        string moduleId,
        string profileId)
    {
        using (var launcherDocument = JsonDocument.Parse(
                   File.ReadAllText(launcherProfilePath)))
        {
            if (launcherDocument.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "插件 activation launcher profile 必须是数组。");
            }

            var entries = launcherDocument.RootElement.EnumerateArray().ToArray();
            if (entries.Length != 1
                || !string.Equals(
                    ReadRequiredString(entries[0], "profileId"),
                    profileId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    ReadRequiredString(entries[0], "machineProfile"),
                    profileId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    ReadRequiredString(entries[0], "executablePath")
                        .Replace('\\', '/'),
                    "../host/IIoT.Edge.Shell",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "插件 activation launcher profile 身份或宿主入口无效。");
            }
        }

        using var machineDocument = JsonDocument.Parse(
            File.ReadAllText(machineConfigPath));
        var root = machineDocument.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !string.Equals(
                ReadRequiredString(root, "instanceId"),
                profileId,
                StringComparison.OrdinalIgnoreCase)
            || !TryGetProperty(root, "shell", out var shell)
            || !string.Equals(
                ReadRequiredString(shell, "machineProfile"),
                profileId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "插件 activation machine config 身份无效。");
        }

        if (!TryGetProperty(root, "modules", out var modules)
            || !TryGetProperty(modules, "enabled", out var enabled)
            || enabled.ValueKind != JsonValueKind.Array
            || !TryGetProperty(modules, moduleId, out var moduleConfiguration)
            || moduleConfiguration.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "插件 activation machine config 缺少所属模块配置。");
        }

        var enabledModules = enabled
            .EnumerateArray()
            .Where(static value => value.ValueKind == JsonValueKind.String)
            .Select(static value => value.GetString()?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (enabledModules.Length != 1
            || !string.Equals(
                enabledModules[0],
                moduleId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "插件 activation machine config 只能启用所属模块。");
        }

        if (TryGetProperty(root, "cloudApi", out var cloudApi)
            && (HasNonEmptyOrInvalidString(cloudApi, "clientCode")
                || HasNonEmptyOrInvalidString(cloudApi, "bootstrapSecret")))
        {
            throw new InvalidOperationException(
                "插件 activation machine config 不得携带 Cloud 身份。");
        }
    }

    private static bool HasNonEmptyOrInvalidString(
        JsonElement element,
        string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind != JsonValueKind.String
               || !string.IsNullOrWhiteSpace(value.GetString());
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException($"插件 activation 缺少 {propertyName}。");
        }

        return value.GetString()!.Trim();
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        propertyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string ResolveSafePackagePath(string rootDirectory, string relativePath, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException(errorMessage);
        }

        var root = Path.GetFullPath(rootDirectory);
        var normalizedRelativePath = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(root, normalizedRelativePath));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return fullPath;
    }

    internal static void CommitPreparedDirectory(
        string moduleDirectory,
        string extractDirectory,
        string backupDirectory)
    {
        var parentDirectory = Path.GetDirectoryName(moduleDirectory)
            ?? throw new InvalidOperationException($"插件目录无效: {moduleDirectory}");
        Directory.CreateDirectory(parentDirectory);

        var existingMoved = false;
        if (Directory.Exists(moduleDirectory))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backupDirectory)!);
            Directory.Move(moduleDirectory, backupDirectory);
            existingMoved = true;
        }

        try
        {
            Directory.Move(extractDirectory, moduleDirectory);
        }
        catch
        {
            if (existingMoved && !Directory.Exists(moduleDirectory) && Directory.Exists(backupDirectory))
            {
                Directory.Move(backupDirectory, moduleDirectory);
            }

            throw;
        }
    }

    private static void WriteInstallRecord(
        string moduleDirectory,
        EdgePluginVersionRelease release,
        EdgePluginManifest manifest,
        string sha256)
    {
        var record = new
        {
            installSchemaVersion = 1,
            installedAtUtc = DateTime.UtcNow,
            moduleId = release.ModuleId,
            processType = manifest.SupportedProcessType,
            displayName = manifest.DisplayName,
            version = release.PackageVersion,
            hostApiVersion = release.HostApiVersion,
            minHostVersion = release.MinHostVersion,
            maxHostVersion = release.MaxHostVersion,
            packageUrl = release.DownloadUrl,
            packageSha256 = sha256,
            targetRuntime = release.TargetRuntime,
            targetFramework = release.TargetFramework,
            signature = release.Signature,
            publisher = release.Publisher
        };
        File.WriteAllText(
            Path.Combine(moduleDirectory, "install.json"),
            JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal static void TryDeleteDirectory(string path)
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
}

internal sealed record PreparedEdgePluginPackage(
    string ModuleId,
    string Version,
    string PackageSha256,
    string StagingRoot,
    string ExtractDirectory,
    IReadOnlyList<PreparedEdgePluginActivationProfile> ActivationProfiles);

internal sealed record PreparedEdgePluginActivationProfile(
    string ProfileId,
    string MachineConfigPath);

public sealed record EdgePluginPackageInstallLimits(
    long MaxPackageBytes,
    long MaxExtractedBytes,
    long MaxEntryBytes,
    int MaxFileCount,
    double MaxCompressionRatio,
    TimeSpan DownloadTimeout)
{
    public static EdgePluginPackageInstallLimits Default { get; } = new(
        MaxPackageBytes: 512L * 1024 * 1024,
        MaxExtractedBytes: 1024L * 1024 * 1024,
        MaxEntryBytes: 512L * 1024 * 1024,
        MaxFileCount: 4096,
        MaxCompressionRatio: 100d,
        DownloadTimeout: TimeSpan.FromMinutes(5));
}
