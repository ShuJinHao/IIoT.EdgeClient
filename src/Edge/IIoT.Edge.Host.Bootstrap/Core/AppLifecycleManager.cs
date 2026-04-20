using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Shell.Core;

public class AppLifecycleManager : IAppLifecycleCoordinator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ShiftConfig _shiftConfig;
    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IRepository<IoMappingEntity> _ioMappings;
    private readonly IProductionContextStore _contextStore;
    private readonly IRecipeService _recipeService;
    private readonly IBackgroundServiceCoordinator _backgroundServices;
    private readonly ILogService _logger;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly IDevelopmentSampleInitializer _developmentSampleInitializer;
    private readonly ICellDataRegistry _cellDataRegistry;
    private readonly IStationRuntimeRegistry _runtimeRegistry;
    private readonly IProcessIntegrationRegistry _integrationRegistry;
    private readonly IStartupDiagnosticsStore _startupDiagnosticsStore;
    private readonly Dictionary<string, IEdgeStationModule> _modulesById;
    private readonly Dictionary<string, ModulePluginDescriptor> _discoveredModulesById;
    private readonly Dictionary<string, IModuleHardwareProfileProvider> _hardwareProfilesByModuleId;
    private readonly IReadOnlyList<ModuleCatalogIssue> _moduleCatalogIssues;
    private readonly string[] _configuredEnabledModuleIds;
    private readonly string[] _activatedModuleIds;

    public AppLifecycleManager(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ShiftConfig shiftConfig,
        IRepository<NetworkDeviceEntity> networkDevices,
        IRepository<IoMappingEntity> ioMappings,
        IProductionContextStore contextStore,
        IRecipeService recipeService,
        IBackgroundServiceCoordinator backgroundServices,
        ILogService logger,
        IPlcConnectionManager plcConnectionManager,
        IDevelopmentSampleInitializer developmentSampleInitializer,
        ICellDataRegistry cellDataRegistry,
        IStationRuntimeRegistry runtimeRegistry,
        IProcessIntegrationRegistry integrationRegistry,
        IStartupDiagnosticsStore startupDiagnosticsStore,
        IReadOnlyCollection<ModulePluginDescriptor> discoveredModules,
        IReadOnlyCollection<ModuleCatalogIssue> moduleCatalogIssues,
        IReadOnlyCollection<string> configuredEnabledModuleIds,
        IEnumerable<IEdgeStationModule> modules,
        IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _shiftConfig = shiftConfig;
        _networkDevices = networkDevices;
        _ioMappings = ioMappings;
        _contextStore = contextStore;
        _recipeService = recipeService;
        _backgroundServices = backgroundServices;
        _logger = logger;
        _plcConnectionManager = plcConnectionManager;
        _developmentSampleInitializer = developmentSampleInitializer;
        _cellDataRegistry = cellDataRegistry;
        _runtimeRegistry = runtimeRegistry;
        _integrationRegistry = integrationRegistry;
        _startupDiagnosticsStore = startupDiagnosticsStore;
        _modulesById = modules.ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);
        _discoveredModulesById = discoveredModules.ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);
        _moduleCatalogIssues = moduleCatalogIssues.ToArray();
        _hardwareProfilesByModuleId = hardwareProfiles.ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);
        _configuredEnabledModuleIds = configuredEnabledModuleIds
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _activatedModuleIds = _modulesById.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<AppStartupResult> StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.Info("[Lifecycle] Starting application bootstrap.");

            _serviceProvider.ApplyMigrations();
            _logger.Info("[Lifecycle] EF Core migrations completed.");

            await _serviceProvider.InitializeDapperTablesAsync();
            _logger.Info("[Lifecycle] Dapper tables initialized.");

            await _developmentSampleInitializer.EnsureConfigurationSamplesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("[Lifecycle] Development sample configuration bootstrap completed.");

            var diagnosticsReport = await BuildStartupDiagnosticsReportAsync(cancellationToken).ConfigureAwait(false);
            _startupDiagnosticsStore.Update(diagnosticsReport);

            if (HasBlockingIssues(diagnosticsReport.Issues))
            {
                var message = BuildValidationMessage(diagnosticsReport.Issues);
                _logger.Error($"[Lifecycle] Startup validation failed.{Environment.NewLine}{message}");
                return AppStartupResult.Failure(message);
            }

            await BindPlcTaskFactoriesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("[Lifecycle] PLC module bindings completed.");

            _contextStore.LoadFromFile();
            _recipeService.LoadFromFile();
            await _developmentSampleInitializer.EnsureRuntimeSamplesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("[Lifecycle] Restored persisted runtime state.");

            await _backgroundServices.StartAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("[Lifecycle] Background services started.");

            _startupDiagnosticsStore.Update(await BuildStartupDiagnosticsReportAsync(cancellationToken).ConfigureAwait(false));
            return AppStartupResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error($"[Lifecycle] Startup failed: {ex.Message}");
            return AppStartupResult.Failure($"Application startup failed: {ex.Message}");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _contextStore.SaveToFile();
        _recipeService.SaveToFile();
        _logger.Info("[Lifecycle] Persisted runtime state before shutdown.");

        await _backgroundServices.StopAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info("[Lifecycle] Background services stopped.");
    }

    private async Task<StartupDiagnosticsReport> BuildStartupDiagnosticsReportAsync(CancellationToken cancellationToken)
    {
        var issues = new List<StartupDiagnosticIssue>();
        issues.AddRange(_moduleCatalogIssues.Select(static issue =>
            new StartupDiagnosticIssue(
                issue.Code,
                issue.Message,
                issue.ModuleId)));

        ValidateAppSettings(issues);
        ValidateModuleConfiguration(issues);

        var plcDevices = await _networkDevices.GetListAsync(
            x => x.IsEnabled && x.DeviceType == DeviceType.PLC,
            cancellationToken).ConfigureAwait(false);
        var deviceBindings = await ValidatePlcConfigurationAsync(plcDevices, issues, cancellationToken).ConfigureAwait(false);
        var configurationProfile = BuildConfigurationProfile();
        var pluginStates = BuildPluginLifecycleSnapshots();
        var moduleRegistrations = BuildModuleRegistrations();

        return new StartupDiagnosticsReport(
            DateTime.UtcNow,
            configurationProfile,
            _discoveredModulesById.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            _configuredEnabledModuleIds,
            _activatedModuleIds,
            pluginStates,
            moduleRegistrations,
            deviceBindings,
            issues.AsReadOnly());
    }

    private void ValidateAppSettings(List<StartupDiagnosticIssue> issues)
    {
        var baseUrl = _configuration["CloudApi:BaseUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", "CloudApi:BaseUrl is missing."));
        }
        else if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"CloudApi:BaseUrl is invalid: {baseUrl}."));
        }

        var clientCode = _configuration["CloudApi:ClientCode"]?.Trim();
        if (string.IsNullOrWhiteSpace(clientCode))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", "CloudApi:ClientCode is missing."));
        }

        if (!TimeSpan.TryParse(_shiftConfig.DayStart, out var dayStart))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"Shift:DayStart is invalid: {_shiftConfig.DayStart}."));
        }

        if (!TimeSpan.TryParse(_shiftConfig.DayEnd, out var dayEnd))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"Shift:DayEnd is invalid: {_shiftConfig.DayEnd}."));
        }

        if (TimeSpan.TryParse(_shiftConfig.DayStart, out dayStart)
            && TimeSpan.TryParse(_shiftConfig.DayEnd, out dayEnd)
            && dayStart == dayEnd)
        {
            issues.Add(CreateIssue("CONFIG_INVALID", "Shift:DayStart and Shift:DayEnd cannot be the same."));
        }

        var configurationProfile = BuildConfigurationProfile();
        if (!string.IsNullOrWhiteSpace(configurationProfile.MachineProfile)
            && !configurationProfile.IsMachineProfileLoaded)
        {
            issues.Add(CreateIssue(
                "MACHINE_PROFILE_MISSING",
                $"Shell machine profile '{configurationProfile.MachineProfile}' was requested, but file '{configurationProfile.MachineProfileFileName}' could not be loaded."));
        }
    }

    private void ValidateModuleConfiguration(List<StartupDiagnosticIssue> issues)
    {
        foreach (var module in _modulesById.Values)
        {
            if (!_cellDataRegistry.IsRegistered(module.ProcessType))
            {
                issues.Add(CreateIssue(
                    "CELLDATA_REGISTRATION_MISSING",
                    $"Module '{module.ModuleId}' is missing CellData registration for process type '{module.ProcessType}'.",
                    module.ModuleId));
            }

            if (!_runtimeRegistry.HasFactory(module.ModuleId))
            {
                issues.Add(CreateIssue(
                    "RUNTIME_FACTORY_MISSING",
                    $"Module '{module.ModuleId}' is missing a PLC runtime factory registration.",
                    module.ModuleId));
            }

            if (!_integrationRegistry.HasCloudUploader(module.ProcessType))
            {
                issues.Add(CreateIssue(
                    "CLOUD_UPLOADER_MISSING",
                    $"Module '{module.ModuleId}' is missing a cloud uploader registration for process type '{module.ProcessType}'.",
                    module.ModuleId));
            }
        }
    }

    private async Task<IReadOnlyList<DeviceModuleBindingSnapshot>> ValidatePlcConfigurationAsync(
        IReadOnlyCollection<NetworkDeviceEntity> plcDevices,
        List<StartupDiagnosticIssue> issues,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<DeviceModuleBindingSnapshot>(plcDevices.Count);

        foreach (var device in plcDevices)
        {
            var deviceName = string.IsNullOrWhiteSpace(device.DeviceName)
                ? $"Id={device.Id}"
                : device.DeviceName;

            var mappings = await _ioMappings.GetListAsync(
                x => x.NetworkDeviceId == device.Id,
                cancellationToken).ConfigureAwait(false);

            var moduleExists = !string.IsNullOrWhiteSpace(device.ModuleId)
                && _discoveredModulesById.ContainsKey(device.ModuleId);
            var moduleEnabled = !string.IsNullOrWhiteSpace(device.ModuleId)
                && _modulesById.ContainsKey(device.ModuleId);

            snapshots.Add(new DeviceModuleBindingSnapshot(
                deviceName,
                device.ModuleId,
                moduleExists,
                moduleEnabled,
                mappings.Count > 0));

            if (string.IsNullOrWhiteSpace(device.DeviceName))
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    "An enabled PLC device is missing DeviceName.",
                    device.DeviceName,
                    deviceName));
            }

            if (string.IsNullOrWhiteSpace(device.ModuleId))
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    $"PLC '{deviceName}' is missing ModuleId.",
                    device.ModuleId,
                    deviceName));
            }
            else if (!moduleExists)
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    $"PLC '{deviceName}' references an unknown module id '{device.ModuleId}'.",
                    device.ModuleId,
                    deviceName));
            }
            else if (!moduleEnabled)
            {
                issues.Add(CreateIssue(
                    "MODULE_NOT_ENABLED",
                    $"PLC '{deviceName}' references module '{device.ModuleId}', but that module is not enabled.",
                    device.ModuleId,
                    deviceName));
            }
            else
            {
                var module = _modulesById[device.ModuleId];

                if (!_runtimeRegistry.HasFactory(module.ModuleId))
                {
                    issues.Add(CreateIssue(
                        "RUNTIME_FACTORY_MISSING",
                        $"PLC '{deviceName}' uses module '{module.ModuleId}', but its runtime factory is not registered.",
                        module.ModuleId,
                        deviceName));
                }

                if (!_cellDataRegistry.IsRegistered(module.ProcessType))
                {
                    issues.Add(CreateIssue(
                        "CELLDATA_REGISTRATION_MISSING",
                        $"PLC '{deviceName}' uses module '{module.ModuleId}', but its CellData is not registered.",
                        module.ModuleId,
                        deviceName));
                }
            }

            if (string.IsNullOrWhiteSpace(device.DeviceModel)
                || !Enum.TryParse<PlcType>(device.DeviceModel, ignoreCase: true, out _))
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODEL_INVALID",
                    $"PLC '{deviceName}' has an invalid DeviceModel: {device.DeviceModel ?? "<empty>"}.",
                    device.ModuleId,
                    deviceName));
            }

            if (string.IsNullOrWhiteSpace(device.IpAddress))
            {
                issues.Add(CreateIssue(
                    "CONFIG_INVALID",
                    $"PLC '{deviceName}' is missing IpAddress.",
                    device.ModuleId,
                    deviceName));
            }

            if (device.Port1 <= 0 || device.Port1 > 65535)
            {
                issues.Add(CreateIssue(
                    "CONFIG_INVALID",
                    $"PLC '{deviceName}' has an invalid Port1 value: {device.Port1}.",
                    device.ModuleId,
                    deviceName));
            }

            if (device.ConnectTimeout <= 0)
            {
                issues.Add(CreateIssue(
                    "CONFIG_INVALID",
                    $"PLC '{deviceName}' must have ConnectTimeout > 0.",
                    device.ModuleId,
                    deviceName));
            }

            if (mappings.Count == 0)
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    $"PLC '{deviceName}' has no IO mappings configured.",
                    device.ModuleId,
                    deviceName));
                continue;
            }

            if (mappings.Any(x => string.IsNullOrWhiteSpace(x.PlcAddress)))
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    $"PLC '{deviceName}' has IO mappings with empty PlcAddress.",
                    device.ModuleId,
                    deviceName));
            }

            if (mappings.Any(x => x.AddressCount <= 0))
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    $"PLC '{deviceName}' has IO mappings with AddressCount <= 0.",
                    device.ModuleId,
                    deviceName));
            }

            if (mappings.Any(x => x.Direction is not ("Read" or "Write")))
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    $"PLC '{deviceName}' has IO mappings with invalid Direction values.",
                    device.ModuleId,
                    deviceName));
            }

            if (!string.IsNullOrWhiteSpace(device.ModuleId)
                && _hardwareProfilesByModuleId.TryGetValue(device.ModuleId, out var provider))
            {
                var validationResult = provider.ValidatePlcConfiguration(
                    deviceName,
                    device.DeviceModel,
                    mappings.Select(static x => new ModuleIoSnapshot(
                            x.Label,
                            x.PlcAddress,
                            x.AddressCount,
                            x.DataType,
                            x.Direction,
                            x.SortOrder))
                        .ToArray());

                if (!validationResult.IsValid)
                {
                    issues.AddRange(validationResult.Issues.Select(issue =>
                        CreateIssue(
                            "HARDWARE_PROFILE_INVALID",
                            issue.Message,
                            device.ModuleId,
                            deviceName)));
                }
            }
        }

        return snapshots;
    }

    private IReadOnlyList<ModuleRegistrationSnapshot> BuildModuleRegistrations()
    {
        return _discoveredModulesById.Values
            .OrderBy(x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ModuleRegistrationSnapshot(
                x.ModuleId,
                x.ProcessType,
                x.AssemblyName,
                _modulesById.ContainsKey(x.ModuleId),
                _cellDataRegistry.IsRegistered(x.ProcessType),
                _runtimeRegistry.HasFactory(x.ModuleId),
                _integrationRegistry.HasCloudUploader(x.ProcessType),
                _integrationRegistry.HasMesUploader(x.ProcessType),
                _hardwareProfilesByModuleId.ContainsKey(x.ModuleId)))
            .ToArray();
    }

    private ConfigurationProfileSnapshot BuildConfigurationProfile()
    {
        var environmentName = _configuration["Shell:Environment"]?.Trim();
        if (string.IsNullOrWhiteSpace(environmentName))
        {
            environmentName = "Production";
        }

        var machineProfile = _configuration["Shell:MachineProfile"]?.Trim();
        var machineProfileFileName = _configuration["Shell:MachineProfileFileName"]?.Trim();
        var machineProfileLoaded = bool.TryParse(_configuration["Shell:MachineProfileLoaded"], out var loaded)
            && loaded;

        return new ConfigurationProfileSnapshot(
            environmentName,
            string.IsNullOrWhiteSpace(machineProfile) ? null : machineProfile,
            string.IsNullOrWhiteSpace(machineProfileFileName) ? null : machineProfileFileName,
            machineProfileLoaded);
    }

    private IReadOnlyList<PluginLifecycleSnapshot> BuildPluginLifecycleSnapshots()
    {
        var configuredEnabledSet = _configuredEnabledModuleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issueLookup = _moduleCatalogIssues
            .Where(static issue => !string.IsNullOrWhiteSpace(issue.ModuleId))
            .GroupBy(issue => issue.ModuleId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var snapshots = _discoveredModulesById.Values
            .OrderBy(descriptor => descriptor.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(descriptor => BuildPluginLifecycleSnapshot(descriptor, configuredEnabledSet, issueLookup))
            .ToList();

        foreach (var issue in _moduleCatalogIssues.Where(static issue => string.Equals(issue.Code, "PLUGIN_MANIFEST_INVALID", StringComparison.OrdinalIgnoreCase)))
        {
            var moduleId = issue.ModuleId
                ?? issue.PluginDirectoryName
                ?? "UnknownPlugin";
            if (snapshots.Any(x => string.Equals(x.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            snapshots.Add(new PluginLifecycleSnapshot(
                moduleId,
                issue.PluginDirectoryName ?? moduleId,
                null,
                "--",
                PluginLifecycleState.ManifestInvalid,
                issue.Message));
        }

        return snapshots
            .OrderBy(snapshot => snapshot.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private PluginLifecycleSnapshot BuildPluginLifecycleSnapshot(
        ModulePluginDescriptor descriptor,
        IReadOnlySet<string> configuredEnabledSet,
        IReadOnlyDictionary<string, ModuleCatalogIssue[]> issueLookup)
    {
        var message = "Plugin was discovered successfully.";
        var state = PluginLifecycleState.Discovered;

        if (issueLookup.TryGetValue(descriptor.ModuleId, out var moduleIssues))
        {
            var hostIssue = moduleIssues.FirstOrDefault(static issue =>
                string.Equals(issue.Code, "PLUGIN_HOST_VERSION_INCOMPATIBLE", StringComparison.OrdinalIgnoreCase));
            if (hostIssue is not null)
            {
                return new PluginLifecycleSnapshot(
                    descriptor.ModuleId,
                    descriptor.DisplayName,
                    descriptor.ProcessType,
                    descriptor.Version,
                    PluginLifecycleState.HostVersionIncompatible,
                    hostIssue.Message);
            }

            var dependencyIssue = moduleIssues.FirstOrDefault(static issue =>
                string.Equals(issue.Code, "PLUGIN_DEPENDENCY_MISSING", StringComparison.OrdinalIgnoreCase));
            if (dependencyIssue is not null)
            {
                return new PluginLifecycleSnapshot(
                    descriptor.ModuleId,
                    descriptor.DisplayName,
                    descriptor.ProcessType,
                    descriptor.Version,
                    PluginLifecycleState.DependencyMissing,
                    dependencyIssue.Message);
            }

            var loadIssue = moduleIssues.FirstOrDefault(static issue =>
                string.Equals(issue.Code, "PLUGIN_LOAD_FAILED", StringComparison.OrdinalIgnoreCase));
            if (loadIssue is not null)
            {
                return new PluginLifecycleSnapshot(
                    descriptor.ModuleId,
                    descriptor.DisplayName,
                    descriptor.ProcessType,
                    descriptor.Version,
                    PluginLifecycleState.LoadFailed,
                    loadIssue.Message);
            }
        }

        if (!configuredEnabledSet.Contains(descriptor.ModuleId))
        {
            state = PluginLifecycleState.DisabledByConfig;
            message = "Plugin was discovered, but it is not enabled by the current configuration.";
        }
        else if (_modulesById.ContainsKey(descriptor.ModuleId))
        {
            state = PluginLifecycleState.Activated;
            message = "Plugin is enabled and activated.";
        }

        return new PluginLifecycleSnapshot(
            descriptor.ModuleId,
            descriptor.DisplayName,
            descriptor.ProcessType,
            descriptor.Version,
            state,
            message);
    }

    private async Task BindPlcTaskFactoriesAsync(CancellationToken cancellationToken)
    {
        var plcDevices = await _networkDevices.GetListAsync(
            x => x.IsEnabled && x.DeviceType == DeviceType.PLC,
            cancellationToken).ConfigureAwait(false);

        foreach (var device in plcDevices)
        {
            if (string.IsNullOrWhiteSpace(device.DeviceName)
                || string.IsNullOrWhiteSpace(device.ModuleId)
                || !_runtimeRegistry.TryGetFactory(device.ModuleId, out var factory))
            {
                continue;
            }

            _plcConnectionManager.RegisterTasks(
                device.DeviceName,
                (buffer, context) => factory.CreateTasks(_serviceProvider, buffer, context));
        }
    }

    private static StartupDiagnosticIssue CreateIssue(
        string code,
        string message,
        string? moduleId = null,
        string? deviceName = null)
        => new(code, message, moduleId, deviceName);

    private static string BuildValidationMessage(IReadOnlyCollection<StartupDiagnosticIssue> issues)
    {
        if (issues.Count == 0)
        {
            return "Startup validation failed.";
        }

        return "Startup validation failed:" + Environment.NewLine
            + string.Join(Environment.NewLine, issues.Select(x =>
            {
                var scope = new List<string>();
                if (!string.IsNullOrWhiteSpace(x.ModuleId))
                {
                    scope.Add($"Module={x.ModuleId}");
                }

                if (!string.IsNullOrWhiteSpace(x.DeviceName))
                {
                    scope.Add($"Device={x.DeviceName}");
                }

                var scopeText = scope.Count == 0 ? string.Empty : $" ({string.Join(", ", scope)})";
                return $"- [{x.Code}]{scopeText} {x.Message}";
            }));
    }

    private static bool HasBlockingIssues(IReadOnlyCollection<StartupDiagnosticIssue> issues)
        => issues.Any(static issue => !string.Equals(issue.Code, "DEVICE_MODEL_INVALID", StringComparison.OrdinalIgnoreCase));
}
