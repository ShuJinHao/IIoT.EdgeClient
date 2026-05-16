using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Host.Bootstrap.Modules;

namespace IIoT.Edge.Host.Bootstrap;

public interface IStartupDiagnosticsModuleRegistrationSnapshotBuilder
{
    IReadOnlyList<ModuleRegistrationSnapshot> Build(
        IReadOnlyDictionary<string, ModulePluginDescriptor> discoveredModulesById,
        IReadOnlyDictionary<string, IEdgeProcessModule> modulesById,
        ICellDataRegistry cellDataRegistry,
        IStationRuntimeRegistry runtimeRegistry,
        IProcessIntegrationRegistry integrationRegistry,
        IReadOnlyDictionary<string, IModuleHardwareProfileProvider> hardwareProfilesByModuleId);
}

internal sealed class StartupDiagnosticsModuleRegistrationSnapshotBuilder : IStartupDiagnosticsModuleRegistrationSnapshotBuilder
{
    public IReadOnlyList<ModuleRegistrationSnapshot> Build(
        IReadOnlyDictionary<string, ModulePluginDescriptor> discoveredModulesById,
        IReadOnlyDictionary<string, IEdgeProcessModule> modulesById,
        ICellDataRegistry cellDataRegistry,
        IStationRuntimeRegistry runtimeRegistry,
        IProcessIntegrationRegistry integrationRegistry,
        IReadOnlyDictionary<string, IModuleHardwareProfileProvider> hardwareProfilesByModuleId)
        => discoveredModulesById.Values
            .OrderBy(x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ModuleRegistrationSnapshot(
                x.ModuleId,
                x.ProcessType,
                x.AssemblyName,
                modulesById.ContainsKey(x.ModuleId),
                cellDataRegistry.IsRegistered(x.ProcessType),
                runtimeRegistry.HasFactory(x.ModuleId),
                integrationRegistry.HasCloudUploader(x.ProcessType),
                integrationRegistry.HasMesUploader(x.ProcessType),
                hardwareProfilesByModuleId.ContainsKey(x.ModuleId)))
            .ToArray();
}
