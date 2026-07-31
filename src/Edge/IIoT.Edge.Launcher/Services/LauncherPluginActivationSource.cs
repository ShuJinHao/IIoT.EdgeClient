using System.Diagnostics;
using System.Security;
using System.Text.Json;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherPluginActivationSource
{
    IReadOnlyList<LauncherPluginActivation> LoadActivations();
}

public sealed record LauncherPluginActivation(
    string ModuleId,
    string ProfileId,
    string LauncherProfilePath,
    string MachineConfigPath);

public sealed class LauncherPluginActivationSource : ILauncherPluginActivationSource
{
    private const string PluginManifestFileName = "plugin.json";
    private const string ActivationDirectoryName = "activation";
    private const string ActivationManifestFileName = "manifest.json";
    private readonly string _baseDirectory;
    private readonly ILauncherEnabledPluginSelectionSource _selectionSource;
    private readonly ILauncherStartupDiagnosticWriter? _diagnostics;

    public LauncherPluginActivationSource(
        string baseDirectory,
        ILauncherEnabledPluginSelectionSource? selectionSource = null,
        ILauncherStartupDiagnosticWriter? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = baseDirectory;
        _diagnostics = diagnostics;
        _selectionSource = selectionSource
            ?? new LauncherEnabledPluginSelectionSource(baseDirectory, diagnostics);
    }

    public IReadOnlyList<LauncherPluginActivation> LoadActivations()
    {
        var selection = _selectionSource.Load();
        if (selection.ModuleIds.Count == 0)
        {
            _diagnostics?.ReplaceArea(
                LauncherStartupDiagnosticAreas.PluginActivationDiscovery,
                []);
            return [];
        }

        var pluginsRoot = EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(_baseDirectory);
        if (!Directory.Exists(pluginsRoot))
        {
            ReplaceDiscoveryDiagnostics(
                new[]
                {
                    CreateDiagnostic(
                        "LAUNCHER_PLUGIN_ROOT_MISSING",
                        subject: null,
                        exceptionType: null)
                }
                .Concat(CreateMissingPluginDiagnostics(
                    selection,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                .ToArray());
            return [];
        }

        var activations = new List<LauncherPluginActivation>();
        var discoveryDiagnostics = new List<LauncherStartupDiagnostic>();
        var discoveredModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] pluginDirectories;
        try
        {
            pluginDirectories = Directory
                .EnumerateDirectories(pluginsRoot)
                .Where(static path => !IsTransientDirectory(path))
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or SecurityException
                                       or ArgumentException
                                       or NotSupportedException)
        {
            ReplaceDiscoveryDiagnostics(
                new[]
                {
                    CreateDiagnostic(
                        "LAUNCHER_PLUGIN_ROOT_ENUMERATION_FAILED",
                        subject: null,
                        exceptionType: ex.GetType().Name)
                }
                .Concat(CreateMissingPluginDiagnostics(
                    selection,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase)))
                .ToArray());
            return [];
        }

        foreach (var pluginDirectory in pluginDirectories)
        {
            var pluginDirectoryName = Path.GetFileName(pluginDirectory);
            if (!selection.TryGetByPluginDirectory(pluginDirectoryName, out var selectedPlugin))
            {
                continue;
            }

            try
            {
                var pluginActivations = LoadPluginActivations(
                    pluginDirectory,
                    selectedPlugin);
                activations.AddRange(pluginActivations);
                if (pluginActivations.Count > 0)
                {
                    discoveredModuleIds.Add(selectedPlugin.ModuleId);
                }
            }
            catch (Exception ex) when (ex is IOException
                                           or UnauthorizedAccessException
                                           or SecurityException
                                           or JsonException
                                           or InvalidOperationException
                                           or ArgumentException
                                           or NotSupportedException)
            {
                var subject = Path.GetFileName(pluginDirectory);
                Trace.TraceWarning(
                    "忽略无效插件 activation：{0} ({1})",
                    subject,
                    ex.GetType().Name);
                discoveryDiagnostics.Add(CreateDiagnostic(
                    "LAUNCHER_PLUGIN_ACTIVATION_INVALID",
                    subject,
                    ex.GetType().Name));
            }
        }

        discoveryDiagnostics.AddRange(
            CreateMissingPluginDiagnostics(selection, discoveredModuleIds));
        ReplaceDiscoveryDiagnostics(discoveryDiagnostics);
        return activations;
    }

    private static IReadOnlyList<LauncherPluginActivation> LoadPluginActivations(
        string pluginDirectory,
        LauncherEnabledPluginSelectionItem selectedPlugin)
    {
        var pluginManifestPath = Path.Combine(pluginDirectory, PluginManifestFileName);
        if (!File.Exists(pluginManifestPath))
        {
            return [];
        }

        using var pluginDocument = JsonDocument.Parse(File.ReadAllText(pluginManifestPath));
        var pluginModuleId = ReadRequiredString(pluginDocument.RootElement, "moduleId");
        if (!string.Equals(
                pluginModuleId,
                selectedPlugin.ModuleId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "plugin.json moduleId 与选择清单目录身份不一致。");
        }

        var activationRoot = Path.Combine(pluginDirectory, ActivationDirectoryName);
        var activationManifestPath = Path.Combine(activationRoot, ActivationManifestFileName);
        if (!File.Exists(activationManifestPath))
        {
            return [];
        }

        using var activationDocument = JsonDocument.Parse(File.ReadAllText(activationManifestPath));
        var root = activationDocument.RootElement;
        if (!TryGetProperty(root, "schemaVersion", out var schemaVersion)
            || schemaVersion.ValueKind != JsonValueKind.Number
            || schemaVersion.GetInt32() != 1)
        {
            throw new InvalidOperationException("activation manifest schemaVersion 必须为 1。");
        }

        var activationModuleId = ReadRequiredString(root, "moduleId");
        if (!string.Equals(pluginModuleId, activationModuleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("activation moduleId 与 plugin.json 不一致。");
        }

        if (!TryGetProperty(root, "profiles", out var profiles)
            || profiles.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("activation manifest 缺少 profiles。");
        }

        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<LauncherPluginActivation>();
        foreach (var profile in profiles.EnumerateArray())
        {
            var profileId = ReadRequiredString(profile, "profileId");
            if (!profileIds.Add(profileId))
            {
                throw new InvalidOperationException($"activation profileId 重复：{profileId}。");
            }

            var launcherProfilePath = ResolveSafeActivationPath(
                activationRoot,
                ReadRequiredString(profile, "launcherProfile"));
            var machineConfigPath = ResolveSafeActivationPath(
                activationRoot,
                ReadRequiredString(profile, "machineConfig"));
            if (!File.Exists(launcherProfilePath) || !File.Exists(machineConfigPath))
            {
                throw new InvalidOperationException($"activation profile '{profileId}' 引用文件不存在。");
            }

            var activation = new LauncherPluginActivation(
                pluginModuleId,
                profileId,
                launcherProfilePath,
                machineConfigPath);
            ValidateActivationFiles(activation);
            result.Add(activation);
        }

        return result;
    }

    private static IEnumerable<LauncherStartupDiagnostic> CreateMissingPluginDiagnostics(
        LauncherEnabledPluginSelection selection,
        IReadOnlySet<string> discoveredModuleIds)
        => selection.Plugins
            .Where(plugin => !discoveredModuleIds.Contains(plugin.ModuleId))
            .Select(plugin => CreateDiagnostic(
                "LAUNCHER_PLUGIN_SELECTED_NOT_DISCOVERED",
                plugin.ModuleId,
                exceptionType: null));

    private void ReplaceDiscoveryDiagnostics(
        IReadOnlyCollection<LauncherStartupDiagnostic> values)
        => _diagnostics?.ReplaceArea(
            LauncherStartupDiagnosticAreas.PluginActivationDiscovery,
            values);

    private static LauncherStartupDiagnostic CreateDiagnostic(
        string reasonCode,
        string? subject,
        string? exceptionType)
        => new(
            LauncherStartupDiagnosticAreas.PluginActivationDiscovery,
            reasonCode,
            LauncherStartupDiagnosticRepairTargets.PluginActivation,
            subject,
            exceptionType);

    internal static void ValidateActivationFiles(LauncherPluginActivation activation)
    {
        using (var launcherDocument = JsonDocument.Parse(File.ReadAllText(activation.LauncherProfilePath)))
        {
            if (launcherDocument.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("插件 launcher profile 必须是数组。");
            }

            var entries = launcherDocument.RootElement.EnumerateArray().ToArray();
            if (entries.Length != 1)
            {
                throw new InvalidOperationException("每个 activation launcher profile 必须只声明一个 profile。");
            }

            var profileId = ReadRequiredString(entries[0], "profileId");
            var machineProfile = ReadRequiredString(entries[0], "machineProfile");
            var executablePath = ReadRequiredString(entries[0], "executablePath")
                .Replace('\\', '/');
            if (!string.Equals(profileId, activation.ProfileId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(machineProfile, activation.ProfileId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(executablePath, "../host/IIoT.Edge.Shell", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("activation launcher profile 身份或宿主入口无效。");
            }
        }

        using var machineDocument = JsonDocument.Parse(File.ReadAllText(activation.MachineConfigPath));
        var machineRoot = machineDocument.RootElement;
        var instanceId = ReadRequiredString(machineRoot, "instanceId");
        if (!string.Equals(instanceId, activation.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("activation machine config 的 InstanceId 必须与 profileId 一致。");
        }

        if (!TryGetProperty(machineRoot, "shell", out var shell)
            || !string.Equals(
                ReadRequiredString(shell, "machineProfile"),
                activation.ProfileId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "activation machine config 的 Shell.MachineProfile 必须与 profileId 一致。");
        }

        if (!TryGetProperty(machineRoot, "modules", out var modules)
            || !TryGetProperty(modules, "enabled", out var enabled)
            || enabled.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("activation machine config 缺少 Modules.Enabled。");
        }

        var moduleIds = enabled
            .EnumerateArray()
            .Where(static value => value.ValueKind == JsonValueKind.String)
            .Select(static value => value.GetString()?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        if (moduleIds.Length != 1
            || !string.Equals(moduleIds[0], activation.ModuleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("activation machine config 只能启用所属模块。");
        }
        if (!TryGetProperty(modules, activation.ModuleId, out var moduleConfiguration)
            || moduleConfiguration.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("activation machine config 缺少所属模块配置。");
        }

        if (TryGetProperty(machineRoot, "cloudApi", out var cloudApi)
            && (HasNonEmptyOrInvalidString(cloudApi, "clientCode")
                || HasNonEmptyOrInvalidString(cloudApi, "bootstrapSecret")))
        {
            throw new InvalidOperationException("activation machine config 不得携带 Cloud 身份。");
        }
    }

    private static string ResolveSafeActivationPath(string activationRoot, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains('\0'))
        {
            throw new InvalidOperationException("activation 路径必须是安全相对路径。");
        }

        var normalizedRoot = Path.GetFullPath(activationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(
            activationRoot,
            relativePath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("activation 路径越界。");
        }

        return resolved;
    }

    private static bool IsTransientDirectory(string path)
    {
        var name = Path.GetFileName(path);
        return string.Equals(name, ".staging", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, ".previous", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException($"activation 缺少 {propertyName}。");
        }

        return value.GetString()!.Trim();
    }

    private static bool HasNonEmptyOrInvalidString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind != JsonValueKind.String
            || !string.IsNullOrWhiteSpace(value.GetString());
    }

    internal static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
