using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Security;
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
    public const string MachineConfigPathEnvironmentVariable = "Shell__MachineConfigPath";
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly IModuleCatalog _moduleCatalog;
    private readonly IEdgeCredentialStore _credentialStore;

    public ShellConfigurationLoader(
        IModuleCatalog? moduleCatalog = null,
        IEdgeCredentialStore? credentialStore = null)
    {
        _moduleCatalog = moduleCatalog
            ?? new DirectoryModuleCatalog(
                new ModulePluginLoader(new ModulePluginAssemblyResolver()),
                new ModulePluginCompatibilityPolicy());
        _credentialStore = credentialStore ?? new WindowsCredentialManagerStore();
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
        var requestedMachineConfigPath = bootstrapConfiguration["Shell:MachineConfigPath"]?.Trim();
        var externalMachineProfilePath = machineProfile is null
            ? null
            : TryResolveExternalMachineProfilePath(
                machineProfile,
                requestedMachineConfigPath,
                normalizedBaseDirectory,
                issues);

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

        var configuration = new ConfigurationBuilder()
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
        AddCredentialBackedSecrets(configuration, externalProfileSettings, issues);
        var authoritativeBinding = BuildAuthoritativeV3BindingProjection(
            normalizedBaseDirectory,
            machineProfile,
            externalProfileSettings);
        configuration.AddInMemoryCollection(authoritativeBinding);
        var configuredRoot = configuration.Build();
        var pluginManifestMetadata = InspectPluginConfigurationContracts(
            normalizedBaseDirectory,
            configuredRoot,
            issues);
        configuration.AddInMemoryCollection(pluginManifestMetadata);
        var configurationRoot = configuration.Build();

        return new ShellConfigurationLoadResult(
            configurationRoot,
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

    private IReadOnlyDictionary<string, string?> InspectPluginConfigurationContracts(
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
            return new Dictionary<string, string?>();

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

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var discoveredConfigurationOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateConfigurationOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                foreach (var configPath in EnumerateLegacyRuntimeConfigurationFiles(descriptor, issues))
                {
                    issues.Add(StartupDiagnosticIssueFactory.Create(
                        "PLUGIN_RUNTIME_CONFIG_IGNORED",
                        $"插件“{descriptor.ModuleId}”携带旧 *.module.json，Host 不再将其作为运行配置源：{configPath}",
                        descriptor.ModuleId));
                }

                var legacyContract = descriptor.ConfigurationContract;
                var privateDatabaseContract = descriptor.PrivateDatabaseContract;
                if (legacyContract is null && privateDatabaseContract is null)
                {
                    issues.Add(StartupDiagnosticIssueFactory.Create(
                        "PLUGIN_MODULE_CONFIGURATION_CONTRACT_MISSING",
                        $"插件“{descriptor.ModuleId}”未声明正式 privateDatabase 或 v2 moduleSeed，已继续启动但不加载任何插件默认配置。",
                        descriptor.ModuleId));
                    continue;
                }

                if (!discoveredConfigurationOwners.Add(descriptor.ModuleId))
                {
                    duplicateConfigurationOwners.Add(descriptor.ModuleId);
                    result.Remove(
                        $"Modules:{descriptor.ModuleId}:Capabilities:RequiresProductionPlan");
                    issues.Add(StartupDiagnosticIssueFactory.Create(
                        "PLUGIN_MODULE_CONFIGURATION_OWNER_DUPLICATE",
                        $"插件“{descriptor.ModuleId}”在多个插件根目录声明正式配置 Owner，已拒绝注入不确定元数据。",
                        descriptor.ModuleId));
                    continue;
                }

                if (legacyContract is not null)
                {
                    var configuredVersion = configuration.GetValue<int?>(
                        $"Modules:{descriptor.ModuleId}:ModuleSeed:Version");
                    var configuredEnvironment = configuration[
                        $"Modules:{descriptor.ModuleId}:ModuleSeed:Environment"]?.Trim();
                    if (configuredVersion != legacyContract.CurrentSeedVersion
                        || string.IsNullOrWhiteSpace(configuredEnvironment)
                        || !legacyContract.SupportedEnvironments.Contains(
                            configuredEnvironment,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        issues.Add(StartupDiagnosticIssueFactory.Create(
                            "PLUGIN_MODULE_SEED_SELECTION_INVALID",
                            $"插件“{descriptor.ModuleId}”的 v2 ModuleSeed 选择无效；要求 v{legacyContract.CurrentSeedVersion}/" +
                            $"{string.Join('|', legacyContract.SupportedEnvironments)}。",
                            descriptor.ModuleId));
                    }
                }

                result[$"Modules:{descriptor.ModuleId}:Capabilities:RequiresProductionPlan"] =
                    (privateDatabaseContract?.RequiresProductionPlan
                     ?? legacyContract!.RequiresProductionPlan).ToString();
            }
        }

        foreach (var duplicateModuleId in duplicateConfigurationOwners)
        {
            result.Remove($"Modules:{duplicateModuleId}:Capabilities:RequiresProductionPlan");
        }

        return result;
    }

    private static IReadOnlyList<string> EnumerateLegacyRuntimeConfigurationFiles(
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
                        "PLUGIN_RUNTIME_CONFIG_PATH_ESCAPE",
                        $"插件“{descriptor.ModuleId}”的旧运行配置真实路径越出 staged 目录，已忽略：{candidate}",
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
                "PLUGIN_RUNTIME_CONFIG_ENUMERATION_FAILED",
                $"无法枚举插件“{descriptor.ModuleId}”的旧运行配置，已继续启动：{ex.Message}",
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
        string? requestedMachineConfigPath,
        string baseDirectory,
        ICollection<StartupDiagnosticIssue> issues)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(requestedMachineConfigPath))
            {
                var candidate = Path.GetFullPath(requestedMachineConfigPath);
                var configRoot = Path.GetFullPath(EdgeClientProgramDataPaths.ResolveConfigRoot(baseDirectory));
                var pluginRoot = Path.GetFullPath(EdgeClientProgramDataPaths.ResolveApplicationPluginRoot(baseDirectory));
                if (!IsWithin(configRoot, candidate) && !IsWithin(pluginRoot, candidate))
                {
                    throw new InvalidDataException("显式机型配置路径越出宿主配置或设备插件目录。");
                }

                return candidate;
            }

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

    private void AddCredentialBackedSecrets(
        IConfigurationBuilder configuration,
        IReadOnlyDictionary<string, string?> externalProfileSettings,
        ICollection<StartupDiagnosticIssue> issues)
    {
        if (!externalProfileSettings.TryGetValue(
                "CloudApi:BootstrapCredentialReference",
                out var reference)
            || string.IsNullOrWhiteSpace(reference))
        {
            return;
        }

        try
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudApi:BootstrapSecret"] = _credentialStore.Read(reference.Trim())
            });
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or InvalidDataException
                                       or InvalidOperationException
                                       or PlatformNotSupportedException
                                       or System.ComponentModel.Win32Exception)
        {
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "BOOTSTRAP_CREDENTIAL_UNAVAILABLE",
                $"设备启动凭证无法从 Windows Credential Manager 读取，Cloud 链路保持关闭：{ex.GetType().Name}。"));
        }
    }

    private static IReadOnlyDictionary<string, string?> BuildAuthoritativeV3BindingProjection(
        string baseDirectory,
        string? machineProfile,
        IReadOnlyDictionary<string, string?> materializedSettings)
    {
        var runtimeBindingPath = EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(baseDirectory);
        if (!File.Exists(runtimeBindingPath))
        {
            return new Dictionary<string, string?>();
        }

        var runtime = EdgeInstallerBindingCodec.ParseRuntime(File.ReadAllText(runtimeBindingPath));
        if (runtime.SchemaVersion != EdgeInstallerBindingCodec.CurrentSchemaVersion)
        {
            return new Dictionary<string, string?>();
        }

        var clientCode = EdgeClientIdentity.NormalizeClientCode(
            string.IsNullOrWhiteSpace(machineProfile)
                ? throw new InvalidDataException("Binding v3 requires a non-empty ClientCode machine profile.")
                : machineProfile);
        var binding = runtime.Bindings.SingleOrDefault(item =>
            string.Equals(item.ClientCode, clientCode, StringComparison.Ordinal))
            ?? throw new InvalidDataException(
                $"Binding v3 has no runtime entry for ClientCode {clientCode}.");
        var expected = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["InstanceId"] = clientCode,
            ["Shell:MachineProfile"] = clientCode,
            ["Shell:ClientCode"] = clientCode,
            ["Shell:RuntimeDataRoot"] = $"plugins/{clientCode}",
            ["Modules:Enabled:0"] = binding.ModuleId,
            ["Modules:PluginRoots:0"] = binding.PluginDirectory,
            ["CloudApi:Enabled"] = "true",
            ["CloudApi:BaseUrl"] = runtime.BaseUrl,
            ["CloudApi:ClientCode"] = clientCode,
            ["CloudApi:BootstrapCredentialReference"] = binding.PendingCredentialReference,
            ["DevicePluginBinding:SchemaVersion"] = EdgeInstallerBindingCodec.CurrentSchemaVersion.ToString(),
            ["DevicePluginBinding:GenerationId"] = runtime.GenerationId,
            ["DevicePluginBinding:ClientCode"] = clientCode,
            ["DevicePluginBinding:DeviceName"] = binding.DeviceName,
            ["DevicePluginBinding:ProcessId"] = binding.ProcessId.ToString("D"),
            ["DevicePluginBinding:ProcessType"] = binding.ProcessType,
            ["DevicePluginBinding:ModuleId"] = binding.ModuleId,
            ["DevicePluginBinding:PluginVersion"] = binding.PluginVersion,
            ["DevicePluginBinding:PackageSha256"] = binding.PackageSha256
        };
        foreach (var descriptor in EdgeBindingRouteCatalog.All)
        {
            expected[$"CloudApi:Paths:{descriptor.MachineConfigKey}"] =
                EdgeBindingRouteCatalog.Get(runtime.Paths, descriptor.Key);
        }

        foreach (var pair in expected)
        {
            if (!materializedSettings.TryGetValue(pair.Key, out var actual)
                || !string.Equals(actual?.Trim(), pair.Value, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Final Binding v3 machine configuration does not match {pair.Key}.");
            }
        }

        if (materializedSettings.ContainsKey("CloudApi:Paths:PlcSnapshot")
            || materializedSettings.ContainsKey("CloudApi:Paths:PassStationBatch")
            || materializedSettings.ContainsKey("CloudApi:BootstrapSecret"))
        {
            throw new InvalidDataException(
                "Final Binding v3 machine configuration contains a legacy route alias or raw secret.");
        }

        return expected;
    }

    private static bool IsWithin(string root, string candidate)
    {
        var normalizedRoot = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
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
