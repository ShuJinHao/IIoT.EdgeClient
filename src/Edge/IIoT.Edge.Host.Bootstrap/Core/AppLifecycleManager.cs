using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.Runtime.Signals;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Shell.Core;

public class AppLifecycleManager : IAppLifecycleCoordinator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly EdgeRuntimePaths _runtimePaths;
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
    private readonly Dictionary<string, IEdgeProcessModule> _modulesById;
    private readonly Dictionary<string, ModulePluginDescriptor> _discoveredModulesById;
    private readonly Dictionary<string, IModuleHardwareProfileProvider> _hardwareProfilesByModuleId;
    private readonly IReadOnlyList<ModuleCatalogIssue> _moduleCatalogIssues;
    private readonly string[] _configuredEnabledModuleIds;
    private readonly string[] _activatedModuleIds;

    public AppLifecycleManager(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        EdgeRuntimePaths runtimePaths,
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
        IEnumerable<IEdgeProcessModule> modules,
        IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _runtimePaths = runtimePaths;
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
            _logger.Info("[生命周期] 开始应用启动。");

            _serviceProvider.ApplyMigrations();
            _logger.Info("[生命周期] EF Core 迁移完成。");

            await _serviceProvider.InitializeDapperTablesAsync();
            _logger.Info("[生命周期] Dapper 表初始化完成。");

            await _developmentSampleInitializer.EnsureConfigurationSamplesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("[生命周期] 开发样例配置初始化完成。");

            var diagnosticsReport = await BuildStartupDiagnosticsReportAsync(cancellationToken).ConfigureAwait(false);
            _startupDiagnosticsStore.Update(diagnosticsReport);

            if (HasBlockingIssues(diagnosticsReport.Issues))
            {
                var message = BuildValidationMessage(diagnosticsReport.Issues);
                _logger.Error($"[生命周期] 启动校验失败。{Environment.NewLine}{message}");
                return AppStartupResult.Failure(message);
            }

            await BindPlcTaskFactoriesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("[生命周期] PLC 模块绑定完成。");

            _contextStore.LoadFromFile();
            _recipeService.LoadFromFile();
            await _developmentSampleInitializer.EnsureRuntimeSamplesAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("[生命周期] 运行时持久化状态恢复完成。");

            await _backgroundServices.StartAsync(cancellationToken).ConfigureAwait(false);
            _logger.Info("[生命周期] 后台服务已启动。");

            _startupDiagnosticsStore.Update(await BuildStartupDiagnosticsReportAsync(cancellationToken).ConfigureAwait(false));
            return AppStartupResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error($"[生命周期] 启动失败：{ex.Message}");
            return AppStartupResult.Failure($"应用启动失败：{ex.Message}");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _contextStore.SaveToFile();
        _recipeService.SaveToFile();
        _logger.Info("[生命周期] 关闭前运行时状态已保存。");

        await _backgroundServices.StopAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info("[生命周期] 后台服务已停止。");
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
            issues.Add(CreateIssue("CONFIG_INVALID", "CloudApi:BaseUrl 未配置。"));
        }
        else if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"CloudApi:BaseUrl 无效：{baseUrl}。"));
        }

        var clientCode = _configuration["CloudApi:ClientCode"]?.Trim();
        if (string.IsNullOrWhiteSpace(clientCode))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", "CloudApi:ClientCode 未配置。"));
        }

        if (!TimeSpan.TryParse(_shiftConfig.DayStart, out var dayStart))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"Shift:DayStart 无效：{_shiftConfig.DayStart}。"));
        }

        if (!TimeSpan.TryParse(_shiftConfig.DayEnd, out var dayEnd))
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"Shift:DayEnd 无效：{_shiftConfig.DayEnd}。"));
        }

        if (TimeSpan.TryParse(_shiftConfig.DayStart, out dayStart)
            && TimeSpan.TryParse(_shiftConfig.DayEnd, out dayEnd)
            && dayStart == dayEnd)
        {
            issues.Add(CreateIssue("CONFIG_INVALID", "Shift:DayStart 和 Shift:DayEnd 不能相同。"));
        }

        var configurationProfile = BuildConfigurationProfile();
        if (!string.IsNullOrWhiteSpace(configurationProfile.MachineProfile)
            && !configurationProfile.IsMachineProfileLoaded)
        {
            issues.Add(CreateIssue(
                "MACHINE_PROFILE_MISSING",
                $"已请求机型配置“{configurationProfile.MachineProfile}”，但文件“{configurationProfile.MachineProfileFileName}”未加载。"));
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
                    $"模块“{module.ModuleId}”缺少工序类型“{module.ProcessType}”的 CellData 注册。",
                    module.ModuleId));
            }

            if (!_runtimeRegistry.HasFactory(module.ModuleId))
            {
                issues.Add(CreateIssue(
                    "RUNTIME_FACTORY_MISSING",
                    $"模块“{module.ModuleId}”缺少 PLC 运行时工厂注册。",
                    module.ModuleId));
            }

            if (!_integrationRegistry.HasCloudUploader(module.ProcessType))
            {
                issues.Add(CreateIssue(
                    "CLOUD_UPLOADER_MISSING",
                    $"模块“{module.ModuleId}”缺少工序类型“{module.ProcessType}”的云端上传器注册。",
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
                    "已启用的 PLC 设备缺少设备名称。",
                    device.DeviceName,
                    deviceName));
            }

            if (string.IsNullOrWhiteSpace(device.ModuleId))
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    $"PLC“{deviceName}”缺少 ModuleId。",
                    device.ModuleId,
                    deviceName));
            }
            else if (!moduleExists)
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    $"PLC“{deviceName}”引用了未知模块“{device.ModuleId}”。",
                    device.ModuleId,
                    deviceName));
            }
            else if (!moduleEnabled)
            {
                issues.Add(CreateIssue(
                    "MODULE_NOT_ENABLED",
                    $"PLC“{deviceName}”引用模块“{device.ModuleId}”，但该模块未启用。",
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
                        $"PLC“{deviceName}”使用模块“{module.ModuleId}”，但运行时工厂未注册。",
                        module.ModuleId,
                        deviceName));
                }

                if (!_cellDataRegistry.IsRegistered(module.ProcessType))
                {
                    issues.Add(CreateIssue(
                        "CELLDATA_REGISTRATION_MISSING",
                        $"PLC“{deviceName}”使用模块“{module.ModuleId}”，但 CellData 未注册。",
                        module.ModuleId,
                        deviceName));
                }
            }

            if (string.IsNullOrWhiteSpace(device.DeviceModel)
                || !Enum.TryParse<PlcType>(device.DeviceModel, ignoreCase: true, out _))
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODEL_INVALID",
                    $"PLC“{deviceName}”的 DeviceModel 无效：{device.DeviceModel ?? "<空>"}。",
                    device.ModuleId,
                    deviceName));
            }

            if (string.IsNullOrWhiteSpace(device.IpAddress))
            {
                issues.Add(CreateIssue(
                    "CONFIG_INVALID",
                    $"PLC“{deviceName}”缺少 IpAddress。",
                    device.ModuleId,
                    deviceName));
            }

            if (device.Port1 <= 0 || device.Port1 > 65535)
            {
                issues.Add(CreateIssue(
                    "CONFIG_INVALID",
                    $"PLC“{deviceName}”的 Port1 无效：{device.Port1}。",
                    device.ModuleId,
                    deviceName));
            }

            if (device.ConnectTimeout <= 0)
            {
                issues.Add(CreateIssue(
                    "CONFIG_INVALID",
                    $"PLC“{deviceName}”的 ConnectTimeout 必须大于 0。",
                    device.ModuleId,
                    deviceName));
            }

            if (mappings.Count == 0)
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    $"PLC“{deviceName}”没有配置 IO 映射。",
                    device.ModuleId,
                    deviceName));
                continue;
            }

            if (mappings.Any(x => string.IsNullOrWhiteSpace(x.PlcAddress)))
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    $"PLC“{deviceName}”存在 PlcAddress 为空的 IO 映射。",
                    device.ModuleId,
                    deviceName));
            }

            if (mappings.Any(x => x.AddressCount <= 0))
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    $"PLC“{deviceName}”存在 AddressCount 小于等于 0 的 IO 映射。",
                    device.ModuleId,
                    deviceName));
            }

            if (mappings.Any(x => x.Direction is not ("Read" or "Write")))
            {
                issues.Add(CreateIssue(
                    "DEVICE_MODULE_MISMATCH",
                    $"PLC“{deviceName}”存在 Direction 无效的 IO 映射。",
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
                            x.SortOrder,
                            x.Category,
                            x.GroupName,
                            x.DisplayRole))
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
            machineProfileLoaded,
            _runtimePaths.RuntimeDataRoot);
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
                ?? "未知插件";
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
        var message = "插件已发现。";
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
            message = "插件已发现，但当前配置未启用。";
        }
        else if (_modulesById.ContainsKey(descriptor.ModuleId))
        {
            state = PluginLifecycleState.Activated;
            message = "插件已启用并激活。";
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

            var mappings = await _ioMappings.GetListAsync(
                x => x.NetworkDeviceId == device.Id,
                cancellationToken).ConfigureAwait(false);
            var signalBindings = mappings
                .Select(static mapping => new ModuleIoSnapshot(
                    mapping.Label,
                    mapping.PlcAddress,
                    mapping.AddressCount,
                    mapping.DataType,
                    mapping.Direction,
                    mapping.SortOrder,
                    mapping.Category,
                    mapping.GroupName,
                    mapping.DisplayRole))
                .ToArray();

            _plcConnectionManager.RegisterTasks(
                device.DeviceName,
                (buffer, context) =>
                {
                    ProductionContextSignalBindings.Set(context, signalBindings);
                    return factory.CreateTasks(_serviceProvider, buffer, context);
                });
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
            return "启动校验失败。";
        }

        return "启动校验失败：" + Environment.NewLine
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

    private static bool HasBlockingIssues(IReadOnlyCollection<StartupDiagnosticIssue> issues)
        => issues.Any(static issue => !string.Equals(issue.Code, "DEVICE_MODEL_INVALID", StringComparison.OrdinalIgnoreCase));
}
