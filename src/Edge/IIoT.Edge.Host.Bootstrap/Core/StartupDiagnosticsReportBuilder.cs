using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.Module.Contracts.Hardware;

namespace IIoT.Edge.Shell.Core;

public interface IStartupDiagnosticsReportBuilder
{
    Task<StartupDiagnosticsReport> BuildAsync(CancellationToken cancellationToken = default);

    bool HasBlockingIssues(IReadOnlyCollection<StartupDiagnosticIssue> issues);

    string BuildValidationMessage(IReadOnlyCollection<StartupDiagnosticIssue> issues);
}

public sealed class StartupDiagnosticsReportBuilder : IStartupDiagnosticsReportBuilder
{
    private readonly IDevicePluginConfigurationSnapshotAccessor _snapshots;
    private readonly ILocalSystemRuntimeConfigService? _runtimeConfigService;
    private readonly IStartupPluginLifecycleSnapshotBuilder _pluginLifecycleSnapshotBuilder;
    private readonly IReadOnlyDictionary<string, IEdgeProcessModule> _modulesById;
    private readonly IReadOnlyDictionary<string, ModulePluginDescriptor> _discoveredModulesById;
    private readonly IReadOnlyList<ModuleCatalogIssue> _moduleCatalogIssues;
    private readonly IReadOnlyList<StartupDiagnosticIssue> _bootstrapDiagnosticIssues;
    private readonly IReadOnlyDictionary<string, IModuleHardwareProfileProvider> _hardwareProfilesByModuleId;
    private readonly string[] _configuredEnabledModuleIds;
    private readonly string[] _activatedModuleIds;
    private readonly IReadOnlyList<IStartupDiagnosticValidator> _validators;
    private readonly IReadOnlyList<IStartupAsyncDiagnosticValidator> _asyncValidators;
    private readonly IStartupConfigurationProfileBuilder _configurationProfileBuilder;
    private readonly IStartupModuleRegistrationSnapshotBuilder _moduleRegistrationSnapshotBuilder;
    private readonly IReadOnlyList<StartupDiagnosticIssue> _constructionDiagnosticIssues;

    public StartupDiagnosticsReportBuilder(
        IDevicePluginConfigurationSnapshotAccessor snapshots,
        IStartupPluginLifecycleSnapshotBuilder pluginLifecycleSnapshotBuilder,
        IReadOnlyCollection<ModulePluginDescriptor> discoveredModules,
        IReadOnlyCollection<ModuleCatalogIssue> moduleCatalogIssues,
        IReadOnlyCollection<StartupDiagnosticIssue> bootstrapDiagnosticIssues,
        IReadOnlyCollection<string> configuredEnabledModuleIds,
        IEnumerable<IEdgeProcessModule> modules,
        IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles,
        IEnumerable<IStartupDiagnosticValidator> validators,
        IEnumerable<IStartupAsyncDiagnosticValidator> asyncValidators,
        IStartupConfigurationProfileBuilder configurationProfileBuilder,
        IStartupModuleRegistrationSnapshotBuilder moduleRegistrationSnapshotBuilder,
        ILocalSystemRuntimeConfigService? runtimeConfigService = null)
    {
        _snapshots = snapshots;
        _runtimeConfigService = runtimeConfigService;
        _pluginLifecycleSnapshotBuilder = pluginLifecycleSnapshotBuilder;
        _modulesById = modules.ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);
        _discoveredModulesById = discoveredModules.ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);
        _moduleCatalogIssues = moduleCatalogIssues.ToArray();
        _bootstrapDiagnosticIssues = bootstrapDiagnosticIssues.ToArray();
        var constructionDiagnosticIssues = new List<StartupDiagnosticIssue>();
        _hardwareProfilesByModuleId = BuildHardwareProfileIndex(
            hardwareProfiles,
            constructionDiagnosticIssues);
        _constructionDiagnosticIssues = constructionDiagnosticIssues;
        _configuredEnabledModuleIds = configuredEnabledModuleIds
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _activatedModuleIds = _modulesById.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        _validators = validators.ToArray();
        _asyncValidators = asyncValidators.ToArray();
        _configurationProfileBuilder = configurationProfileBuilder;
        _moduleRegistrationSnapshotBuilder = moduleRegistrationSnapshotBuilder;
    }

    public async Task<StartupDiagnosticsReport> BuildAsync(CancellationToken cancellationToken = default)
    {
        var issues = new List<StartupDiagnosticIssue>();
        issues.AddRange(_constructionDiagnosticIssues);
        if (_runtimeConfigService is not null)
        {
            try
            {
                await _runtimeConfigService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                AddDiagnosticFailureIssue(
                    issues,
                    "STARTUP_RUNTIME_CONFIG_DIAGNOSTIC_FAILED",
                    "运行配置初始化诊断失败，已跳过该诊断项。");
            }
        }

        issues.AddRange(_moduleCatalogIssues.Select(static issue =>
            StartupDiagnosticIssueFactory.Create(
                issue.Code,
                issue.Message,
                issue.ModuleId)));
        issues.AddRange(_bootstrapDiagnosticIssues);

        var configurationProfile = _configurationProfileBuilder.Build();
        var plcDevices = await LoadPlcDevicesAsync(issues, cancellationToken).ConfigureAwait(false);
        var context = new StartupValidationContext
        {
            ConfigurationProfile = configurationProfile,
            SystemCloudEnabled = _runtimeConfigService?.Current.SystemCloudEnabled ?? false,
            PlcDevices = plcDevices,
            ModulesById = _modulesById,
            DiscoveredModulesById = _discoveredModulesById,
            HardwareProfilesByModuleId = _hardwareProfilesByModuleId
        };

        foreach (var validator in _validators)
        {
            try
            {
                validator.Validate(context, issues);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                AddDiagnosticFailureIssue(
                    issues,
                    "STARTUP_DIAGNOSTIC_VALIDATOR_FAILED",
                    $"启动诊断项“{validator.GetType().Name}”执行失败，已跳过该诊断项。");
            }
        }

        foreach (var validator in _asyncValidators)
        {
            try
            {
                await validator.ValidateAsync(context, issues, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                AddDiagnosticFailureIssue(
                    issues,
                    "STARTUP_DIAGNOSTIC_VALIDATOR_FAILED",
                    $"启动诊断项“{validator.GetType().Name}”执行失败，已跳过该诊断项。");
            }
        }

        var moduleRegistrations = BuildModuleRegistrations(context, issues);
        var pluginStates = BuildPluginStates(issues);

        return new StartupDiagnosticsReport(
            DateTime.UtcNow,
            context.ConfigurationProfile,
            _discoveredModulesById.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            _configuredEnabledModuleIds,
            _activatedModuleIds,
            pluginStates,
            moduleRegistrations,
            context.DeviceBindings,
            issues.AsReadOnly());
    }

    private Task<IReadOnlyCollection<DevicePluginPlcSnapshot>> LoadPlcDevicesAsync(
        List<StartupDiagnosticIssue> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyCollection<DevicePluginPlcSnapshot> devices = _snapshots.GetPlcs()
                .Where(static item => item.IsEnabled)
                .ToArray();
            return Task.FromResult(devices);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            AddDiagnosticFailureIssue(
                issues,
                "STARTUP_DEVICE_DIAGNOSTIC_FAILED",
                "PLC 设备诊断读取失败，已跳过设备诊断项。");
            return Task.FromResult<IReadOnlyCollection<DevicePluginPlcSnapshot>>([]);
        }
    }

    private IReadOnlyList<PluginLifecycleSnapshot> BuildPluginStates(List<StartupDiagnosticIssue> issues)
    {
        try
        {
            return _pluginLifecycleSnapshotBuilder.Build(
                _discoveredModulesById.Values,
                _moduleCatalogIssues,
                _configuredEnabledModuleIds,
                _modulesById.Keys);
        }
        catch (Exception)
        {
            AddDiagnosticFailureIssue(
                issues,
                "STARTUP_PLUGIN_DIAGNOSTIC_FAILED",
                "插件生命周期诊断生成失败，已跳过该诊断项。");
            return [];
        }
    }

    private IReadOnlyList<ModuleRegistrationSnapshot> BuildModuleRegistrations(
        StartupValidationContext context,
        List<StartupDiagnosticIssue> issues)
    {
        try
        {
            return _moduleRegistrationSnapshotBuilder.Build(context);
        }
        catch (Exception)
        {
            AddDiagnosticFailureIssue(
                issues,
                "STARTUP_MODULE_REGISTRATION_DIAGNOSTIC_FAILED",
                "模块注册诊断生成失败，已跳过该诊断项。");
            return [];
        }
    }

    private static void AddDiagnosticFailureIssue(
        List<StartupDiagnosticIssue> issues,
        string code,
        string message)
        => issues.Add(StartupDiagnosticIssueFactory.Create(code, message));

    internal static IReadOnlyDictionary<string, IModuleHardwareProfileProvider> BuildHardwareProfileIndex(
        IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles,
        ICollection<StartupDiagnosticIssue> issues)
    {
        var result = new Dictionary<string, IModuleHardwareProfileProvider>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in hardwareProfiles)
        {
            string moduleId;
            try
            {
                moduleId = provider.ModuleId;
                if (string.IsNullOrWhiteSpace(moduleId))
                    throw new InvalidOperationException("ModuleId 为空。");
            }
            catch (Exception ex)
            {
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    "HARDWARE_PROFILE_IDENTITY_FAILED",
                    $"硬件 profile 身份读取失败，已忽略该 profile：{ex.Message}"));
                continue;
            }

            if (provider is GuardedModuleHardwareProfileProvider guarded
                && guarded.IdentityFailureMessage is { Length: > 0 } identityFailure)
            {
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    "HARDWARE_PROFILE_IDENTITY_FAILED",
                    $"插件“{moduleId}”的硬件 profile 身份无效：{identityFailure}",
                    moduleId));
            }

            if (duplicates.Contains(moduleId))
                continue;

            if (!result.TryAdd(moduleId, provider))
            {
                result.Remove(moduleId);
                duplicates.Add(moduleId);
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    "HARDWARE_PROFILE_DUPLICATE",
                    $"模块“{moduleId}”重复注册硬件 profile，已忽略该模块的全部 profile，避免不确定选择。",
                    moduleId));
            }
        }

        return result;
    }

    public bool HasBlockingIssues(IReadOnlyCollection<StartupDiagnosticIssue> issues)
        => false;

    public string BuildValidationMessage(IReadOnlyCollection<StartupDiagnosticIssue> issues)
    {
        if (issues.Count == 0)
        {
            return "启动诊断无问题。";
        }

        return "启动诊断问题：" + Environment.NewLine
            + string.Join(Environment.NewLine, issues.Select(x =>
            {
                var scope = new List<string>();
                if (!string.IsNullOrWhiteSpace(x.ModuleId))
                {
                    scope.Add($"模块={x.ModuleId}");
                }

                if (!string.IsNullOrWhiteSpace(x.DeviceName))
                {
                    scope.Add($"设备={x.DeviceName}");
                }

                var scopeText = scope.Count == 0 ? string.Empty : $" ({string.Join(", ", scope)})";
                return $"- [{x.Code}]{scopeText} {x.Message}";
            }));
    }
}
