using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Shell.Core;

/// <summary>
/// Stages every registration made by one plugin and publishes the complete set only
/// after Configure has returned and all cross-plugin conflicts have been checked.
/// </summary>
internal sealed class ModuleRegistrationTransaction(
    IServiceCollection services,
    IViewRegistry viewRegistry,
    IConfiguration configuration,
    CellDataRegistry cellDataRegistry,
    StationRuntimeRegistry runtimeRegistry,
    ProcessIntegrationRegistry integrationRegistry,
    ModuleParamRegistry moduleParamRegistry)
{
    public void Register(IEdgeProcessModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var serviceBaseline = services.ToArray();
        IServiceCollection stagedServices = new ServiceCollection();
        foreach (var descriptor in serviceBaseline)
            stagedServices.Add(descriptor);

        var stagedViews = new ViewRegistry();
        var stagedCellData = new CellDataRegistry(new CellDataTypeRegistry());
        var stagedRuntime = new StationRuntimeRegistry();
        var stagedIntegration = new ProcessIntegrationRegistry();
        var stagedParameters = new BufferedModuleParamRegistry();
        var builder = new EdgeProcessModuleBuilder(
            module.ModuleId,
            module.ProcessType,
            stagedServices,
            configuration,
            new ModuleViewRegistry(stagedViews, module.ModuleId),
            stagedCellData,
            stagedRuntime,
            stagedIntegration,
            stagedParameters);

        module.Configure(builder);

        var serviceAdditions = GetServiceAdditions(serviceBaseline, stagedServices, module.ModuleId);
        Preflight(
            module.ModuleId,
            stagedViews,
            stagedCellData,
            stagedRuntime,
            stagedIntegration,
            stagedParameters);
        Commit(
            module,
            serviceAdditions,
            stagedViews,
            stagedCellData,
            stagedRuntime,
            stagedIntegration,
            stagedParameters);
    }

    private static IReadOnlyList<ServiceDescriptor> GetServiceAdditions(
        IReadOnlyList<ServiceDescriptor> baseline,
        IServiceCollection stagedServices,
        string moduleId)
    {
        if (stagedServices.Count < baseline.Count)
        {
            throw new InvalidOperationException(
                $"插件“{moduleId}”不得删除宿主或其他插件的 DI 注册。");
        }

        for (var index = 0; index < baseline.Count; index++)
        {
            if (!ReferenceEquals(baseline[index], stagedServices[index]))
            {
                throw new InvalidOperationException(
                    $"插件“{moduleId}”不得替换或重排宿主或其他插件的 DI 注册。");
            }
        }

        return stagedServices.Skip(baseline.Count).ToArray();
    }

    private void Preflight(
        string moduleId,
        ViewRegistry stagedViews,
        CellDataRegistry stagedCellData,
        StationRuntimeRegistry stagedRuntime,
        ProcessIntegrationRegistry stagedIntegration,
        BufferedModuleParamRegistry stagedParameters)
    {
        foreach (var registration in stagedViews.GetAllViewRegistrations())
        {
            if (viewRegistry.GetViewRegistration(registration.ViewId) is not null)
            {
                throw new InvalidOperationException(
                    $"插件“{moduleId}”的视图“{registration.ViewId}”与已有注册冲突。");
            }
        }

        foreach (var menu in stagedViews.GetAllMenus())
        {
            if (viewRegistry.GetAllMenus().Any(existing => string.Equals(
                    existing.ViewId,
                    menu.ViewId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"插件“{moduleId}”的菜单“{menu.ViewId}”与已有注册冲突。");
            }
        }

        foreach (var registration in stagedCellData.GetRegistrations())
        {
            if (cellDataRegistry.IsRegistered(registration.Key))
            {
                throw new InvalidOperationException(
                    $"插件“{moduleId}”的 ProcessType“{registration.Key}”已注册 CellData。");
            }
        }

        foreach (var registration in stagedRuntime.GetRegistrations())
        {
            if (runtimeRegistry.HasFactory(registration.Key))
            {
                throw new InvalidOperationException(
                    $"插件“{moduleId}”的运行时工厂“{registration.Key}”已注册。");
            }
        }

        foreach (var registration in stagedIntegration.GetCloudUploaders())
        {
            if (integrationRegistry.HasCloudUploader(registration.Key))
            {
                throw new InvalidOperationException(
                    $"插件“{moduleId}”的云端上传器“{registration.Key}”已注册。");
            }
        }

        foreach (var registration in stagedIntegration.GetMesUploaders())
        {
            if (integrationRegistry.HasMesUploader(registration.Key))
            {
                throw new InvalidOperationException(
                    $"插件“{moduleId}”的 MES 上传器“{registration.Key}”已注册。");
            }
        }

        var existingParameterModuleIds = moduleParamRegistry.GetRegistrations()
            .Select(static registration => registration.ModuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var request in stagedParameters.Requests)
        {
            if (existingParameterModuleIds.Contains(request.ModuleId))
            {
                throw new InvalidOperationException(
                    $"插件“{moduleId}”的参数枚举“{request.ModuleId}”已注册。");
            }
        }
    }

    private void Commit(
        IEdgeProcessModule module,
        IReadOnlyList<ServiceDescriptor> serviceAdditions,
        ViewRegistry stagedViews,
        CellDataRegistry stagedCellData,
        StationRuntimeRegistry stagedRuntime,
        ProcessIntegrationRegistry stagedIntegration,
        BufferedModuleParamRegistry stagedParameters)
    {
        foreach (var registration in stagedCellData.GetRegistrations())
            cellDataRegistry.Register(registration.Key, registration.Value);

        foreach (var registration in stagedRuntime.GetRegistrations().Values)
            runtimeRegistry.Register(registration);

        foreach (var registration in stagedIntegration.GetCloudUploaders().Values)
            integrationRegistry.RegisterCloudUploader(registration.ProcessType, registration.UploadMode);

        foreach (var registration in stagedIntegration.GetMesUploaders().Values)
            integrationRegistry.RegisterMesUploader(registration.ProcessType, registration.UploadMode);

        foreach (var request in stagedParameters.Requests)
        {
            moduleParamRegistry.Register(
                request.ModuleId,
                request.MesParamType,
                request.CloudParamType,
                request.BusinessParamType,
                request.DefaultOverrides);
        }

        CommitViews(stagedViews);

        services.AddSingleton(module);
        foreach (var descriptor in serviceAdditions)
            services.Add(descriptor);
    }

    private void CommitViews(ViewRegistry stagedViews)
    {
        var anchorables = stagedViews.GetAllAnchorables();
        var anchorableIds = anchorables
            .Select(static info => info.ContentId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var registration in stagedViews.GetAllViewRegistrations()
                     .Where(registration => !anchorableIds.Contains(registration.ViewId)))
        {
            if (registration.ViewModelFactory is null)
            {
                viewRegistry.RegisterRoute(
                    registration.ViewId,
                    registration.ViewType,
                    registration.ViewModelType,
                    registration.CacheView);
            }
            else
            {
                viewRegistry.RegisterRoute(
                    registration.ViewId,
                    registration.ViewType,
                    registration.ViewModelType,
                    registration.ViewModelFactory,
                    registration.CacheView);
            }
        }

        foreach (var info in anchorables)
        {
            var registration = stagedViews.GetViewRegistration(info.ContentId)!;
            if (registration.ViewModelFactory is null)
            {
                viewRegistry.RegisterAnchorable(
                    info,
                    registration.ViewType,
                    registration.ViewModelType,
                    registration.CacheView);
            }
            else
            {
                viewRegistry.RegisterAnchorable(
                    info,
                    registration.ViewType,
                    registration.ViewModelType,
                    registration.ViewModelFactory,
                    registration.CacheView);
            }
        }

        foreach (var menu in stagedViews.GetAllMenus())
            viewRegistry.RegisterMenu(menu);
    }

    private sealed class BufferedModuleParamRegistry : IModuleParamRegistry
    {
        private readonly ModuleParamRegistry _inner = new();
        private readonly List<ModuleParamRegistrationRequest> _requests = [];

        public IReadOnlyList<ModuleParamRegistrationRequest> Requests => _requests;

        public void Register(
            string moduleId,
            Type mesParamType,
            Type cloudParamType,
            Type businessParamType,
            IReadOnlyCollection<ModuleParamDefaultOverride>? defaultOverrides = null)
        {
            var copiedOverrides = defaultOverrides?
                .Select(static value => value with
                {
                    LegacyDefaultValues = value.LegacyDefaultValues?.ToArray()
                })
                .ToArray();
            _inner.Register(
                moduleId,
                mesParamType,
                cloudParamType,
                businessParamType,
                copiedOverrides);
            _requests.Add(new ModuleParamRegistrationRequest(
                moduleId,
                mesParamType,
                cloudParamType,
                businessParamType,
                copiedOverrides));
        }

        public IReadOnlyList<ModuleParamRegistration> GetRegistrations()
            => _inner.GetRegistrations();

        public IReadOnlyList<ModuleParamDescriptor> GetDescriptors(ModuleParamCategory category)
            => _inner.GetDescriptors(category);

        public IReadOnlyList<ModuleParamDescriptor> GetDescriptors(string moduleId, ModuleParamCategory category)
            => _inner.GetDescriptors(moduleId, category);

        public bool TryGetRegistration(
            Type mesParamType,
            Type cloudParamType,
            Type businessParamType,
            out ModuleParamRegistration registration)
            => _inner.TryGetRegistration(
                mesParamType,
                cloudParamType,
                businessParamType,
                out registration);
    }

    internal sealed record ModuleParamRegistrationRequest(
        string ModuleId,
        Type MesParamType,
        Type CloudParamType,
        Type BusinessParamType,
        IReadOnlyCollection<ModuleParamDefaultOverride>? DefaultOverrides);
}
