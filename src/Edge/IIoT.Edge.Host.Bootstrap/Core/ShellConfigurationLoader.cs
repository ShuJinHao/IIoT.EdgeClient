using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.SharedKernel.Configuration;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json;

namespace IIoT.Edge.Shell.Core;

public sealed record ShellConfigurationLoadResult(
    IConfigurationRoot Configuration,
    string EnvironmentName,
    string? MachineProfile,
    string? MachineProfileFileName,
    bool IsMachineProfileLoaded)
{
    public string? MachineProfilePath { get; init; }

    public string? ExternalMachineProfilePath { get; init; }

    public bool IsExternalMachineProfileLoaded { get; init; }

    public IReadOnlyList<StartupDiagnosticIssue> Issues { get; init; } = [];
}

public interface IShellConfigurationLoader
{
    ShellConfigurationLoadResult Load(string baseDirectory);
}

public sealed class ShellConfigurationLoader : IShellConfigurationLoader
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly IModuleCatalog _moduleCatalog;

    public ShellConfigurationLoader(IModuleCatalog? moduleCatalog = null)
    {
        _moduleCatalog = moduleCatalog
            ?? new DirectoryModuleCatalog(
                new ModulePluginLoader(new ModulePluginAssemblyResolver()),
                new ModulePluginCompatibilityPolicy());
    }

    public ShellConfigurationLoadResult Load(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var issues = new List<StartupDiagnosticIssue>();
        var normalizedBaseDirectory = Path.GetFullPath(baseDirectory);
        var environmentName = GetEnvironmentName(issues);
        var baseSettings = ReadJsonSettings(
            Path.Combine(normalizedBaseDirectory, "appsettings.json"),
            "APPSETTINGS_BASE_UNAVAILABLE",
            required: true,
            issues);
        var environmentSettings = ReadJsonSettings(
            Path.Combine(normalizedBaseDirectory, $"appsettings.{environmentName}.json"),
            "APPSETTINGS_ENVIRONMENT_INVALID",
            required: false,
            issues);
        var bootstrapConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(baseSettings)
            .AddInMemoryCollection(environmentSettings)
            .AddEnvironmentVariables()
            .Build();

        var requestedMachineProfile = bootstrapConfiguration["Shell:MachineProfile"]?.Trim();
        var machineProfile = IsSafeFileNameSegment(requestedMachineProfile)
            ? requestedMachineProfile
            : null;
        if (!string.IsNullOrWhiteSpace(requestedMachineProfile) && machineProfile is null)
        {
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "MACHINE_PROFILE_NAME_INVALID",
                $"机型配置名称包含非法路径字符，已忽略并使用 Default：{requestedMachineProfile}。"));
        }

        var machineProfileFileName = machineProfile is null
            ? null
            : $"appsettings.machine.{machineProfile}.json";
        var packagedMachineProfilePath = machineProfileFileName is null
            ? null
            : Path.Combine(normalizedBaseDirectory, machineProfileFileName);
        var externalMachineProfilePath = machineProfile is null
            ? null
            : TryResolveExternalMachineProfilePath(machineProfile, normalizedBaseDirectory, issues);

        if (externalMachineProfilePath is not null)
        {
            TryInitializeExternalMachineProfile(
                packagedMachineProfilePath,
                externalMachineProfilePath,
                issues);
        }

        var packagedProfileSettings = ReadOptionalProfileSettings(
            packagedMachineProfilePath,
            "MACHINE_PROFILE_PACKAGED_INVALID",
            issues,
            out var packagedMachineProfileLoaded);
        var externalProfileSettings = ReadOptionalProfileSettings(
            externalMachineProfilePath,
            "MACHINE_PROFILE_EXTERNAL_INVALID",
            issues,
            out var externalMachineProfileLoaded);
        var machineProfileLoaded = externalMachineProfileLoaded || packagedMachineProfileLoaded;
        var effectiveMachineProfilePath = externalMachineProfileLoaded
            ? externalMachineProfilePath
            : packagedMachineProfileLoaded
                ? packagedMachineProfilePath
                : null;

        var configuration = new ConfigurationBuilder();
        foreach (var pluginDefaults in FindPluginDefaultConfigurations(
                     normalizedBaseDirectory,
                     bootstrapConfiguration,
                     issues))
        {
            configuration.AddInMemoryCollection(pluginDefaults);
        }

        configuration
            .AddInMemoryCollection(baseSettings)
            .AddInMemoryCollection(environmentSettings)
            .AddInMemoryCollection(packagedProfileSettings)
            .AddInMemoryCollection(externalProfileSettings)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shell:Environment"] = environmentName,
                ["Shell:MachineProfile"] = machineProfile,
                ["Shell:MachineProfileFileName"] = machineProfileFileName,
                ["Shell:MachineProfileLoaded"] = machineProfileLoaded.ToString(),
                ["Shell:MachineProfilePath"] = effectiveMachineProfilePath,
                ["Shell:ExternalMachineProfilePath"] = externalMachineProfilePath,
                ["Shell:ExternalMachineProfileLoaded"] = externalMachineProfileLoaded.ToString()
            });

        return new ShellConfigurationLoadResult(
            configuration.Build(),
            environmentName,
            machineProfile,
            machineProfileFileName,
            machineProfileLoaded)
        {
            MachineProfilePath = effectiveMachineProfilePath,
            ExternalMachineProfilePath = externalMachineProfilePath,
            IsExternalMachineProfileLoaded = externalMachineProfileLoaded,
            Issues = issues
        };
    }

    private static string GetEnvironmentName(ICollection<StartupDiagnosticIssue> issues)
    {
        var requested = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";
        var environmentName = requested.Trim();
        if (IsSafeFileNameSegment(environmentName))
            return environmentName;

        issues.Add(StartupDiagnosticIssueFactory.Create(
            "SHELL_ENVIRONMENT_NAME_INVALID",
            $"运行环境名称包含非法路径字符，已回退到 Production：{requested}。"));
        return "Production";
    }

    private IReadOnlyList<IReadOnlyDictionary<string, string?>> FindPluginDefaultConfigurations(
        string baseDirectory,
        IConfiguration configuration,
        ICollection<StartupDiagnosticIssue> issues)
    {
        var enabledModuleIds = configuration
            .GetSection("Modules:Enabled")
            .Get<string[]>()
            ?.Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (enabledModuleIds.Count == 0)
            return [];

        var configuredRoots = configuration
            .GetSection("Modules:PluginRoots")
            .Get<string[]>()
            ?? [];
        var pluginRoots = new List<string>();
        foreach (var configuredRoot in configuredRoots.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            try
            {
                pluginRoots.Add(EdgeClientProgramDataPaths.ResolveConfiguredPluginRoot(
                    baseDirectory,
                    configuredRoot));
            }
            catch (Exception ex) when (StartupExceptionBoundary.IsApprovedPathFailure(ex))
            {
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    "PLUGIN_ROOT_PATH_INVALID",
                    $"插件根目录配置无效，已忽略“{configuredRoot}”：{ex.Message}"));
            }
        }

        if (pluginRoots.Count == 0)
            pluginRoots.Add(EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(baseDirectory));

        var result = new List<IReadOnlyDictionary<string, string?>>();
        foreach (var pluginRoot in pluginRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(pluginRoot))
                continue;

            var discovery = _moduleCatalog.DiscoverModules(pluginRoot);

            foreach (var discoveryIssue in discovery.Issues)
            {
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    discoveryIssue.Code,
                    discoveryIssue.Message,
                    discoveryIssue.ModuleId));
            }

            foreach (var descriptor in discovery.Modules
                         .Where(descriptor => enabledModuleIds.Contains(descriptor.ModuleId)))
            {
                foreach (var configPath in EnumeratePluginDefaultFiles(descriptor, issues))
                {
                    var settings = ReadJsonSettings(
                        configPath,
                        "PLUGIN_DEFAULT_CONFIG_INVALID",
                        required: true,
                        issues);
                    var requiredPrefix = $"Modules:{descriptor.ModuleId}";
                    var scopedSettings = settings
                        .Where(pair => pair.Key.StartsWith(
                            requiredPrefix + ":",
                            StringComparison.OrdinalIgnoreCase))
                        .ToDictionary(
                            static pair => pair.Key,
                            static pair => pair.Value,
                            StringComparer.OrdinalIgnoreCase);
                    if (settings.Keys.Any(key => !key.StartsWith(
                            requiredPrefix + ":",
                            StringComparison.OrdinalIgnoreCase)))
                    {
                        issues.Add(StartupDiagnosticIssueFactory.Create(
                            "PLUGIN_DEFAULT_SCOPE_REJECTED",
                            $"插件“{descriptor.ModuleId}”的默认配置包含宿主或其他插件键，越界键已忽略：{configPath}",
                            descriptor.ModuleId));
                    }

                    if (scopedSettings.Count > 0)
                        result.Add(scopedSettings);
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<string> EnumeratePluginDefaultFiles(
        ModulePluginDescriptor descriptor,
        ICollection<StartupDiagnosticIssue> issues)
    {
        try
        {
            var candidates = Directory.EnumerateFiles(
                    descriptor.PluginDirectory,
                    "*.module.json",
                    SearchOption.TopDirectoryOnly)
                .Concat(Directory.Exists(Path.Combine(descriptor.PluginDirectory, "Config"))
                    ? Directory.EnumerateFiles(
                        Path.Combine(descriptor.PluginDirectory, "Config"),
                        "*.module.json",
                        SearchOption.TopDirectoryOnly)
                    : []);
            var result = new List<string>();
            foreach (var candidate in candidates.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                var physicalPath = PluginPathBoundary.ResolveExistingPhysicalPath(candidate);
                if (!PluginPathBoundary.IsWithin(descriptor.PluginDirectory, physicalPath))
                {
                    issues.Add(StartupDiagnosticIssueFactory.Create(
                        "PLUGIN_DEFAULT_PATH_ESCAPE",
                        $"插件“{descriptor.ModuleId}”的默认配置真实路径越出 staged 目录，已忽略：{candidate}",
                        descriptor.ModuleId));
                    continue;
                }

                result.Add(physicalPath);
            }

            return result;
        }
        catch (Exception ex) when (StartupExceptionBoundary.IsApprovedPathFailure(ex))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "PLUGIN_DEFAULT_ENUMERATION_FAILED",
                $"无法枚举插件“{descriptor.ModuleId}”的默认配置，已继续启动：{ex.Message}",
                descriptor.ModuleId));
            return [];
        }
    }

    private static IReadOnlyDictionary<string, string?> ReadOptionalProfileSettings(
        string? path,
        string issueCode,
        ICollection<StartupDiagnosticIssue> issues,
        out bool loaded)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            loaded = false;
            return new Dictionary<string, string?>();
        }

        var issueCount = issues.Count;
        var settings = ReadJsonSettings(path, issueCode, required: true, issues);
        loaded = issues.Count == issueCount;
        return loaded ? settings : new Dictionary<string, string?>();
    }

    private static IReadOnlyDictionary<string, string?> ReadJsonSettings(
        string path,
        string issueCode,
        bool required,
        ICollection<StartupDiagnosticIssue> issues)
    {
        if (!File.Exists(path))
        {
            if (required)
            {
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    issueCode,
                    $"配置文件不存在，已使用安全空配置继续启动：{path}"));
            }

            return new Dictionary<string, string?>();
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), JsonOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("根节点必须是 JSON 对象。");

            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            Flatten(document.RootElement, parentPath: null, result);
            return result;
        }
        catch (Exception ex) when (StartupExceptionBoundary.IsApprovedPathFailure(ex)
                                   || ex is JsonException)
        {
            issues.Add(StartupDiagnosticIssueFactory.Create(
                issueCode,
                $"配置文件无法读取或解析，已忽略并继续启动：{path}；{ex.Message}"));
            return new Dictionary<string, string?>();
        }
    }

    private static void Flatten(
        JsonElement element,
        string? parentPath,
        IDictionary<string, string?> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(
                        property.Value,
                        string.IsNullOrEmpty(parentPath)
                            ? property.Name
                            : $"{parentPath}:{property.Name}",
                        result);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, $"{parentPath}:{index}", result);
                    index++;
                }
                break;
            case JsonValueKind.Null:
                if (!string.IsNullOrEmpty(parentPath))
                    result[parentPath] = null;
                break;
            case JsonValueKind.String:
                if (!string.IsNullOrEmpty(parentPath))
                    result[parentPath] = element.GetString();
                break;
            default:
                if (!string.IsNullOrEmpty(parentPath))
                    result[parentPath] = element.GetRawText();
                break;
        }
    }

    private static string? TryResolveExternalMachineProfilePath(
        string machineProfile,
        string baseDirectory,
        ICollection<StartupDiagnosticIssue> issues)
    {
        try
        {
            return EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(machineProfile, baseDirectory);
        }
        catch (Exception ex) when (StartupExceptionBoundary.IsApprovedPathFailure(ex))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "MACHINE_PROFILE_EXTERNAL_PATH_INVALID",
                $"无法解析外部机型配置路径，已继续使用基础配置：{ex.Message}"));
            return null;
        }
    }

    private static void TryInitializeExternalMachineProfile(
        string? sourcePath,
        string targetPath,
        ICollection<StartupDiagnosticIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)
            || !File.Exists(sourcePath)
            || File.Exists(targetPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.Copy(sourcePath, targetPath, overwrite: false);
        }
        catch (Exception ex) when (StartupExceptionBoundary.IsApprovedPathFailure(ex))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "MACHINE_PROFILE_EXTERNAL_INITIALIZATION_FAILED",
                $"无法初始化外部机型配置，将继续使用可读配置：{ex.Message}"));
        }
    }

    private static bool IsSafeFileNameSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value is "." or ".."
            || value.Contains("..", StringComparison.Ordinal)
            || value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            return false;
        }

        return value.All(static character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
    }
}
