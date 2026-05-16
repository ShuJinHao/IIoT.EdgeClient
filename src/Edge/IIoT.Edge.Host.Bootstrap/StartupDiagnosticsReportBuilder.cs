using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Host.Bootstrap;

public interface IStartupDiagnosticsReportBuilder
{
    Task<StartupDiagnosticsReport> BuildAsync(CancellationToken cancellationToken = default);

    bool HasBlockingIssues(IReadOnlyCollection<StartupDiagnosticIssue> issues);

    string BuildValidationMessage(IReadOnlyCollection<StartupDiagnosticIssue> issues);
}

public sealed class StartupDiagnosticsReportBuilder : IStartupDiagnosticsReportBuilder
{
    private readonly IConfiguration _configuration;
    private readonly EdgeRuntimePaths _runtimePaths;
    private readonly IStartupDiagnosticsConfigurationValidator _configurationValidator;
    private readonly IStartupDiagnosticsPlcDeviceValidator _plcDeviceValidator;
    private readonly IStartupDiagnosticsModuleRegistrationSnapshotBuilder _moduleRegistrationSnapshotBuilder;
    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IRepository<IoMappingEntity> _ioMappings;
    private readonly ICellDataRegistry _cellDataRegistry;
    private readonly IStationRuntimeRegistry _runtimeRegistry;
    private readonly IProcessIntegrationRegistry _integrationRegistry;
    private readonly ILocalSystemRuntimeConfigService? _runtimeConfigService;
    private readonly IStartupPluginLifecycleSnapshotBuilder _pluginLifecycleSnapshotBuilder;
    private readonly Dictionary<string, IEdgeProcessModule> _modulesById;
    private readonly Dictionary<string, ModulePluginDescriptor> _discoveredModulesById;
    private readonly Dictionary<string, IModuleHardwareProfileProvider> _hardwareProfilesByModuleId;
    private readonly IReadOnlyList<ModuleCatalogIssue> _moduleCatalogIssues;
    private readonly string[] _configuredEnabledModuleIds;
    private readonly string[] _activatedModuleIds;

    public StartupDiagnosticsReportBuilder(
        IConfiguration configuration,
        EdgeRuntimePaths runtimePaths,
        IStartupDiagnosticsConfigurationValidator configurationValidator,
        IStartupDiagnosticsPlcDeviceValidator plcDeviceValidator,
        IStartupDiagnosticsModuleRegistrationSnapshotBuilder moduleRegistrationSnapshotBuilder,
        IRepository<NetworkDeviceEntity> networkDevices,
        IRepository<IoMappingEntity> ioMappings,
        ICellDataRegistry cellDataRegistry,
        IStationRuntimeRegistry runtimeRegistry,
        IProcessIntegrationRegistry integrationRegistry,
        IStartupPluginLifecycleSnapshotBuilder pluginLifecycleSnapshotBuilder,
        IReadOnlyCollection<ModulePluginDescriptor> discoveredModules,
        IReadOnlyCollection<ModuleCatalogIssue> moduleCatalogIssues,
        IReadOnlyCollection<string> configuredEnabledModuleIds,
        IEnumerable<IEdgeProcessModule> modules,
        IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles,
        ILocalSystemRuntimeConfigService? runtimeConfigService = null)
    {
        _configuration = configuration;
        _runtimePaths = runtimePaths;
        _configurationValidator = configurationValidator;
        _plcDeviceValidator = plcDeviceValidator;
        _moduleRegistrationSnapshotBuilder = moduleRegistrationSnapshotBuilder;
        _networkDevices = networkDevices;
        _ioMappings = ioMappings;
        _cellDataRegistry = cellDataRegistry;
        _runtimeRegistry = runtimeRegistry;
        _integrationRegistry = integrationRegistry;
        _runtimeConfigService = runtimeConfigService;
        _pluginLifecycleSnapshotBuilder = pluginLifecycleSnapshotBuilder;
        _modulesById = modules.ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);
        _discoveredModulesById = discoveredModules.ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);
        _moduleCatalogIssues = moduleCatalogIssues.ToArray();
        _hardwareProfilesByModuleId = hardwareProfiles.ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);
        _configuredEnabledModuleIds = configuredEnabledModuleIds
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _activatedModuleIds = _modulesById.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<StartupDiagnosticsReport> BuildAsync(CancellationToken cancellationToken = default)
    {
        if (_runtimeConfigService is not null)
        {
            await _runtimeConfigService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        }

        var issues = new List<StartupDiagnosticIssue>();
        issues.AddRange(_moduleCatalogIssues.Select(static issue =>
            new StartupDiagnosticIssue(
                issue.Code,
                issue.Message,
                issue.ModuleId)));

        _configurationValidator.Validate(issues, _runtimeConfigService?.Current.CloudUploadEnabled ?? true, BuildConfigurationProfile());
        ValidateModuleConfiguration(issues);

        var plcDevices = await _networkDevices.GetListAsync(
            x => x.IsEnabled && x.DeviceType == DeviceType.PLC,
            cancellationToken).ConfigureAwait(false);
        var deviceBindings = await ValidatePlcConfigurationAsync(plcDevices, issues, cancellationToken).ConfigureAwait(false);

        return new StartupDiagnosticsReport(
            DateTime.UtcNow,
            BuildConfigurationProfile(),
            _discoveredModulesById.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            _configuredEnabledModuleIds,
            _activatedModuleIds,
            _pluginLifecycleSnapshotBuilder.Build(
                _discoveredModulesById.Values,
                _moduleCatalogIssues,
                _configuredEnabledModuleIds,
                _modulesById.Keys),
            BuildModuleRegistrations(),
            deviceBindings,
            issues.AsReadOnly());
    }

    public bool HasBlockingIssues(IReadOnlyCollection<StartupDiagnosticIssue> issues)
        => issues.Any(static issue => !string.Equals(issue.Code, "DEVICE_MODEL_INVALID", StringComparison.OrdinalIgnoreCase));

    public string BuildValidationMessage(IReadOnlyCollection<StartupDiagnosticIssue> issues)
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
            var deviceName = string.IsNullOrWhiteSpace(device.DeviceName) ? $"Id={device.Id}" : device.DeviceName;
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

            ValidateDeviceModuleBinding(device, deviceName, mappings, moduleExists, moduleEnabled, issues);
            ValidateHardwareProfile(device, deviceName, mappings, issues);
        }

        return snapshots;
    }

    private void ValidateDeviceModuleBinding(
        NetworkDeviceEntity device,
        string deviceName,
        IReadOnlyCollection<IoMappingEntity> mappings,
        bool moduleExists,
        bool moduleEnabled,
        List<StartupDiagnosticIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(device.DeviceName))
        {
            issues.Add(CreateIssue("DEVICE_MODULE_MISMATCH", "已启用的 PLC 设备缺少设备名称。", device.DeviceName, deviceName));
        }

        if (string.IsNullOrWhiteSpace(device.ModuleId))
        {
            issues.Add(CreateIssue("DEVICE_MODULE_MISMATCH", $"PLC“{deviceName}”缺少 ModuleId。", device.ModuleId, deviceName));
        }
        else if (!moduleExists)
        {
            issues.Add(CreateIssue("DEVICE_MODULE_MISMATCH", $"PLC“{deviceName}”引用了未知模块“{device.ModuleId}”。", device.ModuleId, deviceName));
        }
        else if (!moduleEnabled)
        {
            issues.Add(CreateIssue("MODULE_NOT_ENABLED", $"PLC“{deviceName}”引用模块“{device.ModuleId}”，但该模块未启用。", device.ModuleId, deviceName));
        }
        else
        {
            ValidateEnabledModuleServices(device, deviceName, issues);
        }

        _plcDeviceValidator.ValidateDeviceEndpoint(device, deviceName, issues);
        _plcDeviceValidator.ValidateIoMappings(device, deviceName, mappings, issues);
    }

    private void ValidateEnabledModuleServices(
        NetworkDeviceEntity device,
        string deviceName,
        List<StartupDiagnosticIssue> issues)
    {
        var module = _modulesById[device.ModuleId];
        if (!_runtimeRegistry.HasFactory(module.ModuleId))
        {
            issues.Add(CreateIssue("RUNTIME_FACTORY_MISSING", $"PLC“{deviceName}”使用模块“{module.ModuleId}”，但运行时工厂未注册。", module.ModuleId, deviceName));
        }

        if (!_cellDataRegistry.IsRegistered(module.ProcessType))
        {
            issues.Add(CreateIssue("CELLDATA_REGISTRATION_MISSING", $"PLC“{deviceName}”使用模块“{module.ModuleId}”，但 CellData 未注册。", module.ModuleId, deviceName));
        }
    }

    private void ValidateHardwareProfile(
        NetworkDeviceEntity device,
        string deviceName,
        IReadOnlyCollection<IoMappingEntity> mappings,
        List<StartupDiagnosticIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(device.ModuleId)
            || !_hardwareProfilesByModuleId.TryGetValue(device.ModuleId, out var provider))
        {
            return;
        }

        var validationResult = provider.ValidatePlcConfiguration(
            deviceName,
            device.DeviceModel,
            mappings.Select(static x => new ModuleIoSnapshot(
                    x.SignalKey,
                    x.PlcAddress,
                    x.AddressCount,
                    x.DataType,
                    x.Direction,
                    x.SortOrder,
                    x.Category,
                    x.BusinessGroup,
                    x.SignalName))
                .ToArray());

        if (!validationResult.IsValid)
        {
            issues.AddRange(validationResult.Issues.Select(issue =>
                CreateIssue("HARDWARE_PROFILE_INVALID", issue.Message, device.ModuleId, deviceName)));
        }
    }

    private IReadOnlyList<ModuleRegistrationSnapshot> BuildModuleRegistrations()
        => _moduleRegistrationSnapshotBuilder.Build(
            _discoveredModulesById,
            _modulesById,
            _cellDataRegistry,
            _runtimeRegistry,
            _integrationRegistry,
            _hardwareProfilesByModuleId);

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

    private static StartupDiagnosticIssue CreateIssue(
        string code,
        string message,
        string? moduleId = null,
        string? deviceName = null)
        => new(code, message, moduleId, deviceName);
}
