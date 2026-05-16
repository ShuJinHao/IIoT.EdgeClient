using System.Reflection;
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
        var modules = ResolveModules(services, options);

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
        {
            var loader = sp.GetRequiredService<IAvaloniaXamlStringResourceLoader>();
            return new AvaloniaResourceLanguageService(
                loader.Load(GetResourceAssemblies(modules)),
                storagePath: Path.Combine(options.RuntimePaths.RuntimeDataRoot, "language.json"));
        });
        RegisterModules(
            services,
            options,
            modules,
            viewRegistry,
            cellDataRegistry,
            runtimeRegistry,
            integrationRegistry,
            moduleParamRegistry);
        RegisterAvaloniaViewRegistry(viewRegistry, options.ModuleIds);
        return services;
    }

    public static void RegisterAvaloniaViewRegistry(
        IAvaloniaViewRegistry viewRegistry,
        IReadOnlyCollection<string> moduleIds)
    {
        ArgumentNullException.ThrowIfNull(viewRegistry);
        ArgumentNullException.ThrowIfNull(moduleIds);

        PanelAvaloniaPresentationRegistration.RegisterPanelViews(viewRegistry);
        NavigationAvaloniaPresentationRegistration.RegisterNavigationViews(viewRegistry, moduleIds);
    }

    private static void RegisterModules(
        IServiceCollection services,
        AvaloniaHostBootstrapOptions options,
        IReadOnlyCollection<IEdgeProcessModule> modules,
        IAvaloniaViewRegistry viewRegistry,
        ICellDataRegistry cellDataRegistry,
        IStationRuntimeRegistry runtimeRegistry,
        IProcessIntegrationRegistry integrationRegistry,
        IModuleParamRegistry moduleParamRegistry)
    {
        if (modules.Count == 0)
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

    private static IReadOnlyCollection<Assembly> GetResourceAssemblies(IReadOnlyCollection<IEdgeProcessModule> modules)
    {
        var assemblies = new List<Assembly>();
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is not null)
        {
            assemblies.Add(entryAssembly);
        }

        assemblies.Add(typeof(IIoT.Edge.Presentation.Shell.Avalonia.DependencyInjection).Assembly);
        assemblies.Add(typeof(PanelAvaloniaPresentationRegistration).Assembly);
        assemblies.Add(typeof(NavigationAvaloniaPresentationRegistration).Assembly);
        assemblies.AddRange(modules.Select(static module => module.GetType().Assembly));

        return assemblies.Distinct().ToArray();
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
