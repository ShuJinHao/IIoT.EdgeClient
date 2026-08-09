using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Configuration;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace IIoT.Edge.Launcher.Services;

public sealed class LauncherProfileCatalog : ILauncherProfileCatalog
{
    private static readonly string DefaultExecutablePath = Path.Combine("..", "host", "IIoT.Edge.Shell");
    private const string DefaultIconKind = "Cog";
    private const string DefaultAccentColor = "#0F766E";
    private const string PluginManifestFileName = "plugin.json";

    private readonly string _baseDirectory;
    private readonly string _catalogPath;
    private readonly ILauncherPluginActivationSource _activationSource;
    private readonly ILauncherEnabledPluginSelectionSource _selectionSource;
    private readonly ILauncherPluginActivationReconciler? _activationReconciler;
    private readonly LauncherHostRuntimeResolver _hostRuntimeResolver;

    public LauncherProfileCatalog(
        string baseDirectory,
        string catalogFileName = "launcher.profiles.json",
        ILauncherPluginActivationSource? activationSource = null,
        ILauncherPluginActivationReconciler? activationReconciler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogFileName);

        _baseDirectory = baseDirectory;
        _catalogPath = Path.Combine(baseDirectory, catalogFileName);
        _selectionSource = activationSource is null
            ? new LauncherEnabledPluginSelectionSource(baseDirectory)
            : new LauncherEnabledPluginSelectionSource(baseDirectory);
        _activationSource = activationSource
            ?? new LauncherPluginActivationSource(baseDirectory, _selectionSource);
        _activationReconciler = activationReconciler;
        _hostRuntimeResolver = new LauncherHostRuntimeResolver(baseDirectory, catalogFileName);
    }

    public IReadOnlyList<LauncherProfileDefinition> LoadProfiles()
    {
        var runtimeBindingPath = EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(_baseDirectory);
        if (!File.Exists(runtimeBindingPath) && !File.Exists(_catalogPath))
        {
            throw new FileNotFoundException($"未找到启动器工序清单：'{_catalogPath}'。", _catalogPath);
        }

        var hostRuntime = _hostRuntimeResolver.Resolve();
        var runtimeProfiles = LoadRuntimeBoundProfiles(hostRuntime);
        if (runtimeProfiles is not null)
        {
            return runtimeProfiles;
        }

        if (!File.Exists(_catalogPath))
        {
            throw new FileNotFoundException($"未找到启动器工序清单：'{_catalogPath}'。", _catalogPath);
        }

        var entries = ReadEntries(_catalogPath);
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("启动器工序清单为空。");
        }

        var profiles = entries.Select(entry => Map(entry)).ToList();
        var profileIds = profiles
            .Select(static profile => profile.ProfileId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var activation in _activationSource.LoadActivations())
        {
            if (_activationReconciler is not null && !_activationReconciler.IsReady(activation))
            {
                continue;
            }

            try
            {
                var contributedEntries = ReadEntries(activation.LauncherProfilePath);
                if (contributedEntries.Count != 1
                    || !string.Equals(
                        contributedEntries[0].ProfileId?.Trim(),
                        activation.ProfileId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("activation launcher profile 身份不一致。");
                }

                var profile = Map(
                    contributedEntries[0],
                    executablePathOverride: hostRuntime.ExecutablePath) with
                {
                    ExpectedModuleIds = [activation.ModuleId],
                    ActivationModuleId = activation.ModuleId,
                    ActivationPluginDirectory = activation.PluginDirectory
                };
                if (!profileIds.Add(profile.ProfileId))
                {
                    throw new InvalidOperationException($"Launcher ProfileId 重复：{profile.ProfileId}。");
                }

                profiles.Add(profile);
            }
            catch (Exception ex) when (ex is IOException
                                           or UnauthorizedAccessException
                                           or JsonException
                                           or InvalidOperationException
                                           or ArgumentException)
            {
                Trace.TraceWarning(
                    "忽略无效 Launcher profile activation：{0}/{1} ({2})",
                    activation.ModuleId,
                    activation.ProfileId,
                    ex.GetType().Name);
            }
        }

        return profiles;
    }

    private IReadOnlyList<LauncherProfileDefinition>? LoadRuntimeBoundProfiles(
        LauncherHostRuntimeLocation hostRuntime)
    {
        var runtimeBindingPath = EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(_baseDirectory);
        if (!File.Exists(runtimeBindingPath))
        {
            return null;
        }

        var envelope = EdgeInstallerBindingCodec.ParseRuntime(
            File.ReadAllText(runtimeBindingPath));
        var selection = _selectionSource.Load();
        if (!selection.ManifestIsValid
            || selection.Plugins.Count != envelope.Bindings.Count)
        {
            throw new InvalidOperationException(
                "设备插件启用清单与运行时 Binding 不一致。");
        }
        var profiles = new List<LauncherProfileDefinition>(envelope.Bindings.Count);
        var layoutRoot = Path.GetDirectoryName(
            EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(_baseDirectory))
            ?? _baseDirectory;
        foreach (var binding in envelope.Bindings)
        {
            var clientCode = EdgeClientIdentity.NormalizeClientCode(binding.ClientCode);
            var pluginRoot = EdgeClientProgramDataPaths.ResolveDevicePluginRoot(
                clientCode,
                _baseDirectory);
            var pluginAppDirectory = Path.Combine(pluginRoot, "app");
            if (!selection.TryGetByClientCode(clientCode, out var selectedPlugin)
                || !string.Equals(
                    selectedPlugin.ModuleId,
                    binding.ModuleId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    selectedPlugin.PluginDirectory,
                    clientCode,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    selectedPlugin.Version,
                    binding.PluginVersion,
                    StringComparison.Ordinal)
                || !string.Equals(
                    selectedPlugin.PackageSha256,
                    binding.PackageSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"设备插件 {clientCode} 的启用清单身份不一致。");
            }

            ValidateRuntimePluginManifest(pluginAppDirectory, binding);
            var machineConfigPath = EdgeClientProgramDataPaths.ResolveDevicePluginMachineConfigPath(
                clientCode,
                _baseDirectory);
            if (!File.Exists(machineConfigPath))
            {
                throw new InvalidOperationException(
                    $"设备插件 {clientCode} 缺少运行配置：{machineConfigPath}。");
            }

            profiles.Add(new LauncherProfileDefinition(
                clientCode,
                binding.DeviceName,
                binding.ProcessType,
                ImagePath: null,
                MachineProfile: clientCode,
                hostRuntime.ExecutablePath,
                DefaultIconKind,
                DefaultAccentColor)
            {
                ClientCode = clientCode,
                ProcessId = binding.ProcessId,
                ProcessType = binding.ProcessType,
                PluginVersion = binding.PluginVersion,
                PackageSha256 = binding.PackageSha256,
                MachineConfigPath = machineConfigPath,
                ExpectedModuleIds = [binding.ModuleId],
                ActivationModuleId = binding.ModuleId,
                ActivationPluginDirectory = pluginAppDirectory,
                PluginDisplayPath = NormalizeDisplayPath(
                    Path.GetRelativePath(layoutRoot, pluginAppDirectory)),
                DataDisplayPath = NormalizeDisplayPath(
                    Path.GetRelativePath(layoutRoot, Path.Combine(pluginRoot, "data")))
            });
        }

        return profiles;
    }

    private static void ValidateRuntimePluginManifest(
        string pluginAppDirectory,
        EdgeRuntimeDeviceBinding binding)
    {
        var manifestPath = Path.Combine(pluginAppDirectory, PluginManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"设备插件 {binding.ClientCode} 缺少 plugin.json。");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var moduleId = ReadRequiredManifestValue(root, "moduleId");
        var version = ReadRequiredManifestValue(root, "version");
        var processType = ReadRequiredManifestValue(root, "supportedProcessType");
        if (!string.Equals(moduleId, binding.ModuleId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(version, binding.PluginVersion, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(processType, binding.ProcessType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"设备插件 {binding.ClientCode} 的 manifest 与运行时 Binding 不一致。");
        }

        ValidateRuntimePluginFileManifest(
            pluginAppDirectory,
            binding.ModuleId,
            binding.PluginVersion);
    }

    private static void ValidateRuntimePluginFileManifest(
        string pluginAppDirectory,
        string expectedModuleId,
        string expectedVersion)
    {
        var manifestPath = Path.Combine(pluginAppDirectory, "file-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"设备插件 {expectedModuleId} 缺少 file-manifest.json。");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schemaVersion)
            || schemaVersion.ValueKind != JsonValueKind.Number
            || schemaVersion.GetInt32() != 1
            || !string.Equals(
                ReadRequiredManifestValue(root, "component"),
                expectedModuleId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                ReadRequiredManifestValue(root, "version"),
                expectedVersion,
                StringComparison.Ordinal)
            || !root.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"设备插件 {expectedModuleId} 的 file manifest 头无效。");
        }

        var expected = new Dictionary<string, (long Size, string Sha256)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in files.EnumerateArray())
        {
            var relativePath = NormalizeManifestPath(
                ReadRequiredManifestValue(item, "path"));
            if (!item.TryGetProperty("size", out var sizeElement)
                || sizeElement.ValueKind != JsonValueKind.Number
                || !sizeElement.TryGetInt64(out var size)
                || size < 0)
            {
                throw new InvalidOperationException(
                    $"设备插件 {expectedModuleId} 的 file manifest 大小无效。");
            }

            var sha256 = ReadRequiredManifestValue(item, "sha256");
            if (string.Equals(
                    relativePath,
                    "file-manifest.json",
                    StringComparison.OrdinalIgnoreCase)
                || sha256.Length != 64
                || !sha256.All(Uri.IsHexDigit)
                || !expected.TryAdd(relativePath, (size, sha256)))
            {
                throw new InvalidOperationException(
                    $"设备插件 {expectedModuleId} 的 file manifest 条目无效或重复。");
            }
        }

        foreach (var path in Directory.EnumerateFiles(
                     pluginAppDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relativePath = NormalizeManifestPath(
                Path.GetRelativePath(pluginAppDirectory, path));
            if (string.Equals(
                    relativePath,
                    "file-manifest.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!expected.Remove(relativePath, out var declared))
            {
                throw new InvalidOperationException(
                    $"设备插件 {expectedModuleId} 包含未声明文件。");
            }

            var info = new FileInfo(path);
            if (info.Length != declared.Size)
            {
                throw new InvalidOperationException(
                    $"设备插件 {expectedModuleId} 文件大小与 manifest 不一致。");
            }

            using var stream = File.OpenRead(path);
            var actualSha256 = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actualSha256, declared.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"设备插件 {expectedModuleId} 文件摘要与 manifest 不一致。");
            }
        }

        if (expected.Count != 0)
        {
            throw new InvalidOperationException(
                $"设备插件 {expectedModuleId} 缺少 manifest 声明文件。");
        }
    }

    private static string NormalizeManifestPath(string value)
    {
        var normalized = value.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0
            || Path.IsPathRooted(value)
            || normalized.Split('/').Any(static segment => segment.Length == 0 || segment is "." or "..")
            || normalized.Any(char.IsControl))
        {
            throw new InvalidOperationException("插件 file manifest 路径无效。");
        }

        return normalized;
    }

    private static string ReadRequiredManifestValue(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException($"plugin.json 缺少 {propertyName}。");
        }

        return value.GetString()!.Trim();
    }

    private static List<LauncherProfileFileEntry> ReadEntries(string path)
        => JsonSerializer.Deserialize<List<LauncherProfileFileEntry>>(
               File.ReadAllText(path),
               JsonOptions())
           ?? [];

    private LauncherProfileDefinition Map(
        LauncherProfileFileEntry entry,
        string? executablePathOverride = null)
    {
        if (string.IsNullOrWhiteSpace(entry.ProfileId))
        {
            throw new InvalidOperationException("启动器工序清单包含缺少 ProfileId 的工序。");
        }

        if (string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            throw new InvalidOperationException($"启动器工序 '{entry.ProfileId}' 缺少 DisplayName。");
        }

        if (string.IsNullOrWhiteSpace(entry.MachineProfile))
        {
            throw new InvalidOperationException($"启动器工序 '{entry.ProfileId}' 缺少 MachineProfile。");
        }

        var executablePath = string.IsNullOrWhiteSpace(executablePathOverride)
            ? string.IsNullOrWhiteSpace(entry.ExecutablePath)
                ? ResolvePath(DefaultExecutablePath)
                : ResolvePath(entry.ExecutablePath)
            : executablePathOverride;
        var imagePath = string.IsNullOrWhiteSpace(entry.ImagePath)
            ? null
            : ResolvePath(entry.ImagePath);
        var iconKind = string.IsNullOrWhiteSpace(entry.IconKind)
            ? DefaultIconKind
            : entry.IconKind.Trim();
        var accentColor = string.IsNullOrWhiteSpace(entry.AccentColor)
            ? DefaultAccentColor
            : entry.AccentColor.Trim();
        var machineProfile = entry.MachineProfile.Trim();
        var hostDirectory = Path.GetDirectoryName(executablePath) ?? _baseDirectory;

        return new LauncherProfileDefinition(
            entry.ProfileId.Trim(),
            entry.DisplayName.Trim(),
            entry.Description?.Trim() ?? string.Empty,
            imagePath,
            machineProfile,
            executablePath,
            iconKind,
            accentColor)
        {
            PluginDisplayPath = ResolvePluginDisplayPath(hostDirectory),
            DataDisplayPath = ResolveDataDisplayPath(hostDirectory, machineProfile)
        };
    }

    private string ResolvePath(string path)
    {
        var expanded = NormalizePathSeparators(EdgeClientProgramDataPaths.ExpandProgramDataTokens(path.Trim(), _baseDirectory));
        return Path.GetFullPath(
            Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(_baseDirectory, expanded));
    }

    private static string NormalizePathSeparators(string path)
        => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static string ResolvePluginDisplayPath(string hostDirectory)
    {
        var pluginsDirectory = EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(hostDirectory);
        if (!Directory.Exists(pluginsDirectory))
        {
            return string.Empty;
        }

        var manifestPath = Directory
            .EnumerateFiles(pluginsDirectory, PluginManifestFileName, SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (manifestPath is null)
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            var moduleId = ReadString(root, "moduleId");
            var entryAssembly = ReadString(root, "entryAssembly");
            if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(entryAssembly))
            {
                return NormalizeDisplayPath(Path.GetRelativePath(pluginsDirectory, manifestPath));
            }

            return NormalizeDisplayPath(Path.GetRelativePath(
                Directory.GetParent(hostDirectory)?.FullName ?? hostDirectory,
                Path.GetDirectoryName(manifestPath)!));
        }
        catch (JsonException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string ResolveDataDisplayPath(string hostDirectory, string machineProfile)
    {
        var configPath = ResolveMachineProfileConfigPath(hostDirectory, machineProfile);
        var runtimeDataRoot = ReadRuntimeDataRoot(configPath);
        if (string.IsNullOrWhiteSpace(runtimeDataRoot))
        {
            return NormalizeDisplayPath(EdgeClientProgramDataPaths.ResolveProfileDataRoot(machineProfile, hostDirectory));
        }

        var normalizedRoot = NormalizePathSeparators(
            EdgeClientProgramDataPaths.ExpandProgramDataTokens(runtimeDataRoot, hostDirectory));
        var absoluteRoot = Path.GetFullPath(
            Path.IsPathRooted(normalizedRoot)
                ? normalizedRoot
                : Path.Combine(hostDirectory, normalizedRoot));
        var layoutRoot = Directory.GetParent(hostDirectory)?.FullName ?? hostDirectory;
        return IsUnderDirectory(layoutRoot, absoluteRoot)
            ? NormalizeDisplayPath(Path.GetRelativePath(layoutRoot, absoluteRoot))
            : NormalizeDisplayPath(absoluteRoot);
    }

    private static string ResolveMachineProfileConfigPath(string hostDirectory, string machineProfile)
    {
        var externalConfigPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(machineProfile, hostDirectory);
        return File.Exists(externalConfigPath)
            ? externalConfigPath
            : Path.Combine(hostDirectory, $"appsettings.machine.{machineProfile}.json");
    }

    private static string? ReadRuntimeDataRoot(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!document.RootElement.TryGetProperty("Shell", out var shell))
            {
                return null;
            }

            return ReadString(shell, "RuntimeDataRoot");
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
    }

    private static string NormalizeDisplayPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (Path.IsPathRooted(path) || IsWindowsRootedPath(normalized))
        {
            return normalized;
        }

        return normalized.TrimStart('.', '/');
    }

    private static bool IsUnderDirectory(string parentDirectory, string childPath)
    {
        var parent = Path.GetFullPath(parentDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var child = Path.GetFullPath(childPath);
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWindowsRootedPath(string path)
        => path.Length >= 3
            && char.IsLetter(path[0])
            && path[1] == ':'
            && path[2] == '/';

    private static JsonSerializerOptions JsonOptions()
        => new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

    private sealed class LauncherProfileFileEntry
    {
        public string? ProfileId { get; set; }

        public string? DisplayName { get; set; }

        public string? Description { get; set; }

        public string? ImagePath { get; set; }

        public string? MachineProfile { get; set; }

        public string? ExecutablePath { get; set; }

        public string? IconKind { get; set; }

        public string? AccentColor { get; set; }
    }
}
