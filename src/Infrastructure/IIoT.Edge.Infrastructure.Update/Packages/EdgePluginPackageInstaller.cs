using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Edge.Application.Abstractions.Updates;
using IIoT.Edge.Infrastructure.Update.Cloud;
using IIoT.Edge.Infrastructure.Update.Plugins;
using IIoT.Edge.SharedKernel.Configuration;
using static IIoT.Edge.Infrastructure.Update.Cloud.EdgeUpdateCloudUrl;

namespace IIoT.Edge.Infrastructure.Update.Packages;

public sealed class EdgePluginPackageInstaller : IEdgePluginPackageInstaller
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

    public async Task<EdgePluginInstallResult> InstallAsync(
        EdgeUpdateTarget target,
        EdgePluginVersionRelease release,
        EdgeUpdateCloudApiOptions cloudOptions,
        string hostVersion,
        string hostApiVersion,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(cloudOptions);

        if (!_compatibilityPolicy.IsReleaseCompatible(release, hostVersion, hostApiVersion, out var issue))
        {
            return EdgePluginInstallResult.Failed(issue!);
        }

        var pluginsRoot = EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(target.HostDirectory);
        var moduleDirectory = Path.Combine(
            pluginsRoot,
            EdgeClientProgramDataPaths.SanitizePathSegment(release.ModuleId));
        var stagingRoot = Path.Combine(pluginsRoot, ".staging", Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(stagingRoot, "package.zip");
        var extractDirectory = Path.Combine(stagingRoot, "extract");

        try
        {
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
                return EdgePluginInstallResult.Failed(
                    $"插件包 SHA256 不匹配: {release.ModuleId} {release.PackageVersion}");
            }

            progress?.Report(70);
            ValidateZipEntries(packagePath, extractDirectory, _limits);
            ZipFile.ExtractToDirectory(packagePath, extractDirectory);
            var manifest = ValidateExtractedPackage(extractDirectory, release, hostVersion, hostApiVersion);
            ReplacePluginDirectory(moduleDirectory, extractDirectory);
            WriteInstallRecord(moduleDirectory, release, manifest, actualSha256);
            progress?.Report(100);

            return EdgePluginInstallResult.Succeeded([release.ModuleId]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return EdgePluginInstallResult.Failed($"插件安装失败: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
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
        if (source.IsFile)
        {
            await using var sourceStream = File.OpenRead(source.LocalPath);
            await using var targetStream = File.Create(packagePath);
            await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
            progress?.Report(60);
            return;
        }

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
        if (Uri.TryCreate(packageUrl, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        if (File.Exists(packageUrl))
        {
            return new Uri(Path.GetFullPath(packageUrl));
        }

        return BuildUrl(baseUrl, packageUrl);
    }

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

    private static void ReplacePluginDirectory(string moduleDirectory, string extractDirectory)
    {
        var parentDirectory = Path.GetDirectoryName(moduleDirectory)
            ?? throw new InvalidOperationException($"插件目录无效: {moduleDirectory}");
        var backupDirectory = Path.Combine(parentDirectory, ".previous", $"{Path.GetFileName(moduleDirectory)}-{Guid.NewGuid():N}");
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

        TryDeleteDirectory(backupDirectory);
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

    private static void TryDeleteDirectory(string path)
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
