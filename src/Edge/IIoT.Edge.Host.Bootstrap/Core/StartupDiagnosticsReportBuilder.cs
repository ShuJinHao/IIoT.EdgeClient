using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Shell.Core;

public interface IStartupDiagnosticsReportBuilder
{
    Task<StartupDiagnosticsReport> BuildAsync(CancellationToken cancellationToken = default);

    bool HasBlockingIssues(IReadOnlyCollection<StartupDiagnosticIssue> issues);

    string BuildValidationMessage(IReadOnlyCollection<StartupDiagnosticIssue> issues);
}

public sealed class StartupDiagnosticsReportBuilder : IStartupDiagnosticsReportBuilder
{
    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly ILocalSystemRuntimeConfigService? _runtimeConfigService;
    private readonly IStartupPluginLifecycleSnapshotBuilder _pluginLifecycleSnapshotBuilder;
    private readonly IReadOnlyDictionary<string, IEdgeProcessModule> _modulesById;
    private readonly IReadOnlyDictionary<string, ModulePluginDescriptor> _discoveredModulesById;
    private readonly IReadOnlyList<ModuleCatalogIssue> _moduleCatalogIssues;
    private readonly IReadOnlyDictionary<string, IModuleHardwareProfileProvider> _hardwareProfilesByModuleId;
    private readonly string[] _configuredEnabledModuleIds;
    private readonly string[] _activatedModuleIds;
    private readonly IReadOnlyList<IStartupDiagnosticValidator> _validators;
    private readonly IReadOnlyList<IStartupAsyncDiagnosticValidator> _asyncValidators;
    private readonly IStartupConfigurationProfileBuilder _configurationProfileBuilder;
    private readonly IStartupModuleRegistrationSnapshotBuilder _moduleRegistrationSnapshotBuilder;

    public StartupDiagnosticsReportBuilder(
        IRepository<NetworkDeviceEntity> networkDevices,
        IStartupPluginLifecycleSnapshotBuilder pluginLifecycleSnapshotBuilder,
        IReadOnlyCollection<ModulePluginDescriptor> discoveredModules,
        IReadOnlyCollection<ModuleCatalogIssue> moduleCatalogIssues,
        IReadOnlyCollection<string> configuredEnabledModuleIds,
        IEnumerable<IEdgeProcessModule> modules,
        IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles,
        IEnumerable<IStartupDiagnosticValidator> validators,
        IEnumerable<IStartupAsyncDiagnosticValidator> asyncValidators,
        IStartupConfigurationProfileBuilder configurationProfileBuilder,
        IStartupModuleRegistrationSnapshotBuilder moduleRegistrationSnapshotBuilder,
        ILocalSystemRuntimeConfigService? runtimeConfigService = null)
    {
        _networkDevices = networkDevices;
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
        _validators = validators.ToArray();
        _asyncValidators = asyncValidators.ToArray();
        _configurationProfileBuilder = configurationProfileBuilder;
        _moduleRegistrationSnapshotBuilder = moduleRegistrationSnapshotBuilder;
    }

    public async Task<StartupDiagnosticsReport> BuildAsync(CancellationToken cancellationToken = default)
    {
        if (_runtimeConfigService is not null)
        {
            await _runtimeConfigService.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        }

        var issues = new List<StartupDiagnosticIssue>();
        issues.AddRange(_moduleCatalogIssues.Select(static issue =>
            StartupDiagnosticIssueFactory.Create(
                issue.Code,
                issue.Message,
                issue.ModuleId)));

        var plcDevices = await _networkDevices.GetListAsync(
            x => x.IsEnabled && x.DeviceType == DeviceType.PLC,
            cancellationToken).ConfigureAwait(false);
        var context = new StartupValidationContext
        {
            ConfigurationProfile = _configurationProfileBuilder.Build(),
            CloudUploadEnabled = _runtimeConfigService?.Current.CloudUploadEnabled ?? true,
            PlcDevices = plcDevices,
            ModulesById = _modulesById,
            DiscoveredModulesById = _discoveredModulesById,
            HardwareProfilesByModuleId = _hardwareProfilesByModuleId
        };

        foreach (var validator in _validators)
        {
            validator.Validate(context, issues);
        }

        foreach (var validator in _asyncValidators)
        {
            await validator.ValidateAsync(context, issues, cancellationToken).ConfigureAwait(false);
        }

        return new StartupDiagnosticsReport(
            DateTime.UtcNow,
            context.ConfigurationProfile,
            _discoveredModulesById.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            _configuredEnabledModuleIds,
            _activatedModuleIds,
            _pluginLifecycleSnapshotBuilder.Build(
                _discoveredModulesById.Values,
                _moduleCatalogIssues,
                _configuredEnabledModuleIds,
                _modulesById.Keys),
            _moduleRegistrationSnapshotBuilder.Build(context),
            context.DeviceBindings,
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
}
