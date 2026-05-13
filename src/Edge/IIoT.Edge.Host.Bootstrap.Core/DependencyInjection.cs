using IIoT.Edge.Application;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Common.Time;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Infrastructure.DeviceComm;
using IIoT.Edge.Infrastructure.Integration;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.EfCore;
using IIoT.Edge.Runtime;
using IIoT.Edge.SharedKernel.DataPipeline.Capacity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.IO;

namespace IIoT.Edge.Host.Bootstrap.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddEdgeHostCoreServices(
        this IServiceCollection services,
        EdgeHostCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuration = options.Configuration;
        var runtimePaths = options.RuntimePaths;
        var efDbPath = Path.Combine(runtimePaths.DatabaseDirectory, "edge.db");

        Directory.CreateDirectory(runtimePaths.DatabaseDirectory);
        Directory.CreateDirectory(runtimePaths.ContextDirectory);
        Directory.CreateDirectory(runtimePaths.ExcelDirectory);
        Directory.CreateDirectory(runtimePaths.LogDirectory);
        Directory.CreateDirectory(runtimePaths.RecipeDirectory);

        services.AddSingleton(configuration);
        services.AddSingleton(runtimePaths);
        services.AddSingleton<IHostEnvironment>(
            new EdgeHostCoreEnvironment(options.EnvironmentName, runtimePaths.BaseDirectory));
        services.TryAddSingleton<ILogService, EdgeHostCoreLogService>();
        services.TryAddSingleton<IModuleParamRegistry, ModuleParamRegistry>();
        services.TryAddSingleton<IProcessIntegrationRegistry, ProcessIntegrationRegistry>();

        var productionTimeOptions =
            configuration.GetSection(ProductionTimeOptions.SectionName).Get<ProductionTimeOptions>()
            ?? new ProductionTimeOptions();
        productionTimeOptions.Validate();
        services.AddSingleton(productionTimeOptions);
        services.AddSingleton<IProductionTimeProvider, ProductionTimeProvider>();

        services.Configure<DataPipelineCapacityOptions>(configuration.GetSection(DataPipelineCapacityOptions.SectionName));
        services.AddSingleton(
            configuration.GetSection(DataPipelineRuntimeOptions.SectionName).Get<DataPipelineRuntimeOptions>()
            ?? new DataPipelineRuntimeOptions());

        var shiftConfig = new ShiftConfig();
        configuration.GetSection("Shift").Bind(shiftConfig);
        services.AddSingleton(shiftConfig);

        services.AddEdgeApplication();
        services.AddEfCorePersistenceInfrastructure(efDbPath);
        services.AddDapperPersistenceInfrastructure(runtimePaths.DatabaseDirectory);
        services.AddIntegrationInfrastructure(configuration, runtimePaths);
        services.AddDeviceCommInfrastructure();
        services.AddEdgeRuntime(runtimePaths);

        services.AddMediatR(cfg =>
        {
            var licenseKey = ResolveMediatRLicenseKey(configuration);
            if (!string.IsNullOrWhiteSpace(licenseKey))
            {
                cfg.LicenseKey = licenseKey;
            }

            cfg.RegisterServicesFromAssemblies(typeof(IIoT.Edge.Application.DependencyInjection).Assembly);
        });

        services.AddAutoMapper(
            _ => { },
            [
                typeof(IIoT.Edge.Application.DependencyInjection).Assembly,
                typeof(IIoT.Edge.Infrastructure.Integration.DependencyInjection).Assembly,
                typeof(IIoT.Edge.Infrastructure.DeviceComm.DependencyInjection).Assembly
            ]);

        return services;
    }

    private static string? ResolveMediatRLicenseKey(Microsoft.Extensions.Configuration.IConfiguration configuration)
        => FirstNonEmpty(
            Environment.GetEnvironmentVariable("MediatR__LicenseKey"),
            Environment.GetEnvironmentVariable("MEDIATR_LICENSE_KEY"),
            configuration["MediatR:LicenseKey"]);

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed class EdgeHostCoreEnvironment(string environmentName, string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.IsNullOrWhiteSpace(environmentName)
            ? Environments.Production
            : environmentName.Trim();

        public string ApplicationName { get; set; } = "IIoT.Edge.AvaloniaShell";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
