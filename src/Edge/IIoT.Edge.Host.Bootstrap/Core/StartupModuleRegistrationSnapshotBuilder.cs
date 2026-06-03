using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Host.Bootstrap.Modules;

namespace IIoT.Edge.Shell.Core;

internal sealed class StartupModuleRegistrationSnapshotBuilder(
    ICellDataRegistry cellDataRegistry,
    IStationRuntimeRegistry runtimeRegistry,
    IProcessIntegrationRegistry integrationRegistry)
    : IStartupModuleRegistrationSnapshotBuilder
{
    public IReadOnlyList<ModuleRegistrationSnapshot> Build(StartupValidationContext context)
        => context.DiscoveredModulesById.Values
            .OrderBy(x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ModuleRegistrationSnapshot(
                x.ModuleId,
                x.ProcessType,
                x.AssemblyName,
                context.ModulesById.ContainsKey(x.ModuleId),
                cellDataRegistry.IsRegistered(x.ProcessType),
                runtimeRegistry.HasFactory(x.ModuleId),
                integrationRegistry.HasCloudUploader(x.ProcessType),
                integrationRegistry.HasMesUploader(x.ProcessType),
                context.HardwareProfilesByModuleId.ContainsKey(x.ModuleId)))
            .ToArray();
}
