using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Host.Bootstrap;
using IIoT.Edge.Host.Bootstrap.Plugins;
using IIoT.Edge.Presentation.Navigation.Avalonia;
using IIoT.Edge.Presentation.Panels.Avalonia;
using IIoT.Edge.Presentation.Shell.Avalonia;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.UI.Avalonia;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IIoT.Edge.Host.Bootstrap;

public static class DependencyInjection
{
    public static IServiceCollection AddEdgeHostAvaloniaBootstrap(
        this IServiceCollection services,
        AvaloniaHostBootstrapOptions options)
    {
        var viewRegistry = new AvaloniaViewRegistry();
        var cellDataTypeRegistry = new CellDataTypeRegistry();
        var cellDataRegistry = new CellDataRegistry(cellDataTypeRegistry);
        var runtimeRegistry = new StationRuntimeRegistry();
        var integrationRegistry = new ProcessIntegrationRegistry();
        var moduleParamRegistry = new ModuleParamRegistry();
        var moduleCatalog = new JsonEdgeProcessModuleCatalog(new EdgeProcessModuleCatalogOptions(
            options.PluginDirectories ?? [options.RuntimePaths.BaseDirectory],
            ".dll"));

        services.TryAddSingleton<IAvaloniaViewRegistry>(viewRegistry);
        services.TryAddSingleton<ICellDataTypeRegistry>(cellDataTypeRegistry);
        services.TryAddSingleton<ICellDataRegistry>(cellDataRegistry);
        services.TryAddSingleton<IStationRuntimeRegistry>(runtimeRegistry);
        services.TryAddSingleton<IProcessIntegrationRegistry>(integrationRegistry);
        services.TryAddSingleton<IModuleParamRegistry>(moduleParamRegistry);
        services.TryAddSingleton<IEdgeProcessModuleCatalog>(moduleCatalog);

        services.AddEdgeHostRuntimeServices(new EdgeHostBootstrapOptions(
            options.Configuration,
            options.RuntimePaths,
            options.EnvironmentName));
        services.AddAvaloniaUiShared();
        services.AddShellAvaloniaPresentation();
        services.AddPanelAvaloniaPresentation();
        services.AddNavigationAvaloniaPresentation();
        services.AddSingleton(options);
        services.AddSingleton<IAvaloniaLanguageService>(sp =>
            new AvaloniaResourceLanguageService(sp.GetServices<IAvaloniaResourceContributor>()));
        RegisterModules(
            services,
            options,
            viewRegistry,
            cellDataRegistry,
            runtimeRegistry,
            integrationRegistry,
            moduleParamRegistry);
        return services;
    }

    public static void RegisterAvaloniaViews(IServiceProvider services)
    {
        ShellAvaloniaPresentationRegistration.RegisterShellViews(services);
        PanelAvaloniaPresentationRegistration.RegisterPanelViews(services);
        NavigationAvaloniaPresentationRegistration.RegisterNavigationViews(
            services,
            services.GetRequiredService<AvaloniaHostBootstrapOptions>().ModuleIds);
    }

    private static void RegisterModules(
        IServiceCollection services,
        AvaloniaHostBootstrapOptions options,
        IAvaloniaViewRegistry viewRegistry,
        ICellDataRegistry cellDataRegistry,
        IStationRuntimeRegistry runtimeRegistry,
        IProcessIntegrationRegistry integrationRegistry,
        IModuleParamRegistry moduleParamRegistry)
    {
        var modules = ResolveModules(services, options);
        if (modules.Length == 0)
        {
            return;
        }

        ValidateModuleIdentity(modules);

        foreach (var module in modules)
        {
            services.AddSingleton<IEdgeProcessModule>(module);
            var builder = new AvaloniaEdgeProcessModuleBuilder(
                module.ModuleId,
                module.ProcessType,
                services,
                options.Configuration,
                viewRegistry,
                cellDataRegistry,
                runtimeRegistry,
                integrationRegistry,
                moduleParamRegistry);

            module.Configure(builder);
        }

        ValidateModuleRegistrations(modules, cellDataRegistry, runtimeRegistry, integrationRegistry);
    }

    private static IEdgeProcessModule[] ResolveModules(
        IServiceCollection services,
        AvaloniaHostBootstrapOptions options)
    {
        if (options.Modules is not null)
        {
            return options.Modules.ToArray();
        }

        var catalog = services
            .FirstOrDefault(descriptor => descriptor.ServiceType == typeof(IEdgeProcessModuleCatalog))
            ?.ImplementationInstance as IEdgeProcessModuleCatalog;

        return catalog?.LoadModules().ToArray() ?? [];
    }

    private static void ValidateModuleIdentity(IEnumerable<IEdgeProcessModule> modules)
    {
        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var processTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in modules)
        {
            if (!moduleIds.Add(module.ModuleId))
            {
                throw new InvalidOperationException($"Duplicate ModuleId detected: {module.ModuleId}");
            }

            if (!processTypes.Add(module.ProcessType))
            {
                throw new InvalidOperationException($"Duplicate ProcessType detected: {module.ProcessType}");
            }
        }
    }

    private static void ValidateModuleRegistrations(
        IEnumerable<IEdgeProcessModule> modules,
        ICellDataRegistry cellDataRegistry,
        IStationRuntimeRegistry runtimeRegistry,
        IProcessIntegrationRegistry integrationRegistry)
    {
        foreach (var module in modules)
        {
            if (!cellDataRegistry.IsRegistered(module.ProcessType))
            {
                throw new InvalidOperationException(
                    $"Module '{module.ModuleId}' is missing CellData registration for process type '{module.ProcessType}'.");
            }

            if (!runtimeRegistry.HasFactory(module.ModuleId))
            {
                throw new InvalidOperationException(
                    $"Module '{module.ModuleId}' is missing PLC runtime factory registration.");
            }

            if (!integrationRegistry.HasCloudUploader(module.ProcessType))
            {
                throw new InvalidOperationException(
                    $"Module '{module.ModuleId}' is missing cloud uploader registration for process type '{module.ProcessType}'.");
            }
        }
    }
}
