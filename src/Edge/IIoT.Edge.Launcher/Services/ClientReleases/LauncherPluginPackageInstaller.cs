using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Runtime;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherPluginPackageInstaller
{
    Task<LauncherPluginInstallResult> InstallAsync(
        LauncherProfileDefinition profile,
        LauncherClientPluginRelease release,
        LauncherCloudApiOptions cloudOptions,
        string hostVersion,
        string hostApiVersion,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class LauncherPluginPackageInstaller : ILauncherPluginPackageInstaller
{
    public async Task<LauncherPluginInstallResult> InstallAsync(
        LauncherProfileDefinition profile,
        LauncherClientPluginRelease release,
        LauncherCloudApiOptions cloudOptions,
        string hostVersion,
        string hostApiVersion,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(cloudOptions);

        if (!IsReleaseCompatible(release, hostVersion, hostApiVersion, out var issue))
        {
            return LauncherPluginInstallResult.Failed(issue!);
        }

        var runtimeDirectory = LauncherCloudApiConfigurationResolver.ResolveRuntimeDirectory(profile);
        var moduleDirectory = EdgeClientProgramDataPaths.ResolveProfilePluginDirectory(
            profile.MachineProfile,
            release.ModuleId,
            runtimeDirectory);
        var stagingRoot = Path.Combine(moduleDirectory, ".staging", Guid.NewGuid().ToString("N"));
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
                progress,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var actualSha256 = await ComputeSha256Async(packagePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualSha256, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return LauncherPluginInstallResult.Failed(
                    $"插件包 SHA256 不匹配: {release.ModuleId} {release.Version}");
            }

            progress?.Report(70);
            ValidateZipEntries(packagePath, extractDirectory);
            ZipFile.ExtractToDirectory(packagePath, extractDirectory);
            var manifest = ValidateExtractedPackage(extractDirectory, release, hostVersion, hostApiVersion);
            ReplaceCurrentPlugin(moduleDirectory, extractDirectory);
            WriteInstallRecord(moduleDirectory, release, manifest, actualSha256);
            progress?.Report(100);

            return LauncherPluginInstallResult.Succeeded([release.ModuleId]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return LauncherPluginInstallResult.Failed($"插件安装失败: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    internal static bool IsReleaseCompatible(
        LauncherClientPluginRelease release,
        string hostVersion,
        string hostApiVersion,
        out string? issue)
    {
        if (!string.Equals(release.HostApiVersion, hostApiVersion, StringComparison.OrdinalIgnoreCase))
        {
            issue = $"插件 {release.ModuleId} 要求 HostApiVersion={release.HostApiVersion}，当前宿主为 {hostApiVersion}。";
            return false;
        }

        if (!EdgeClientHostRuntime.TryParseVersion(hostVersion, out var host)
            || !EdgeClientHostRuntime.TryParseVersion(release.MinHostVersion, out var min)
            || !EdgeClientHostRuntime.TryParseVersion(release.MaxHostVersion, out var max)
            || host.CompareTo(min) < 0
            || host.CompareTo(max) > 0)
        {
            issue = $"插件 {release.ModuleId} 兼容宿主版本范围为 [{release.MinHostVersion}, {release.MaxHostVersion}]，当前宿主为 {hostVersion}。";
            return false;
        }

        issue = null;
        return true;
    }

    private static async Task DownloadPackageAsync(
        string packageUrl,
        string baseUrl,
        string packagePath,
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

        using var client = new HttpClient();
        using var response = await client
            .GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var fileStream = File.Create(packagePath);
        var buffer = new byte[64 * 1024];
        long readTotal = 0;
        int read;
        while ((read = await responseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            readTotal += read;
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

        return LauncherEdgeReleaseCloudClient.BuildUrl(baseUrl, packageUrl);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void ValidateZipEntries(string packagePath, string extractDirectory)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            _ = ResolveSafePackagePath(extractDirectory, entry.FullName, $"插件包包含非法路径: {entry.FullName}");
        }
    }

    private static LauncherPluginManifest ValidateExtractedPackage(
        string extractDirectory,
        LauncherClientPluginRelease release,
        string hostVersion,
        string hostApiVersion)
    {
        var manifestPath = Path.Combine(extractDirectory, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException("插件包根目录缺少 plugin.json。");
        }

        var manifest = JsonSerializer.Deserialize<LauncherPluginManifest>(
            File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("plugin.json 无法解析。");
        if (!string.Equals(manifest.ModuleId, release.ModuleId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(manifest.Version, release.Version, StringComparison.OrdinalIgnoreCase)
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

        if (!IsReleaseCompatible(release, hostVersion, hostApiVersion, out var issue))
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

    private static void ReplaceCurrentPlugin(string moduleDirectory, string extractDirectory)
    {
        var currentDirectory = Path.Combine(moduleDirectory, EdgeClientProgramDataPaths.PluginCurrentDirectoryName);
        var previousDirectory = Path.Combine(moduleDirectory, EdgeClientProgramDataPaths.PluginPreviousDirectoryName);
        Directory.CreateDirectory(moduleDirectory);
        TryDeleteDirectory(previousDirectory);

        var currentMoved = false;
        if (Directory.Exists(currentDirectory))
        {
            Directory.Move(currentDirectory, previousDirectory);
            currentMoved = true;
        }

        try
        {
            Directory.Move(extractDirectory, currentDirectory);
        }
        catch
        {
            if (currentMoved && !Directory.Exists(currentDirectory) && Directory.Exists(previousDirectory))
            {
                Directory.Move(previousDirectory, currentDirectory);
            }

            throw;
        }
    }

    private static void WriteInstallRecord(
        string moduleDirectory,
        LauncherClientPluginRelease release,
        LauncherPluginManifest manifest,
        string sha256)
    {
        var record = new
        {
            installSchemaVersion = 1,
            installedAtUtc = DateTime.UtcNow,
            moduleId = release.ModuleId,
            processType = manifest.SupportedProcessType,
            displayName = manifest.DisplayName,
            version = release.Version,
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
