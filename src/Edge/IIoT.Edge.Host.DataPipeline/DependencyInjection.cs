using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Consumers;
using IIoT.Edge.Module.Contracts.DataPipeline.Stores;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Host.DataPipeline.Context;
using IIoT.Edge.Host.DataPipeline.Consumers;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.Host.DataPipeline.Tasks;
using IIoT.Edge.Module.Sdk.Signals;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.Application.Common.DataPipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using IIoT.Edge.Module.Contracts.Mes;
namespace IIoT.Edge.Host.DataPipeline;

public static class DependencyInjection
{
    public static IServiceCollection AddEdgeRuntime(this IServiceCollection services, EdgeRuntimePaths runtimePaths)
    {
        services.TryAddSingleton<ICellDataTypeRegistry, CellDataTypeRegistry>();
        services.TryAddSingleton<ICellDataJsonSerializer, CellDataJsonSerializer>();
        services.TryAddSingleton<IProductionContextCorruptFileQuarantine, ProductionContextCorruptFileQuarantine>();
        services.TryAddSingleton<IProductionContextRuntimeStateCopier, ProductionContextRuntimeStateCopier>();
        services.TryAddSingleton<IProductionContextSignalBindingStore, ProductionContextSignalBindingStore>();
        services.TryAddSingleton<IPlcIdentityAliasRegistry>(sp =>
            new PersistentPlcIdentityAliasRegistry(
                runtimePaths.ContextDirectory,
                sp.GetRequiredService<ILogService>()));
        services.AddSingleton(sp =>
            new ProductionContextStore(
                sp.GetRequiredService<ILogService>(),
                sp.GetServices<IProductionContextFactory>(),
                sp.GetRequiredService<ICellDataTypeRegistry>(),
                new ProductionContextPersistenceFileSystem(),
                runtimePaths.ContextDirectory,
                sp.GetRequiredService<IProductionContextCorruptFileQuarantine>(),
                sp.GetRequiredService<IProductionContextRuntimeStateCopier>(),
                sp.GetRequiredService<IPlcIdentityAliasRegistry>()));
        services.AddSingleton<IProductionContextStore>(sp => sp.GetRequiredService<ProductionContextStore>());
        services.AddSingleton<IPlcProductionContextStore>(sp => sp.GetRequiredService<ProductionContextStore>());
        services.AddSingleton<ITodayCapacityStore, TodayCapacityStore>();

        AddHostDataPipelineCore(services);

        return services;
    }

    private static void AddHostDataPipelineCore(IServiceCollection services)
    {
        services.AddSingleton<DataPipelineCapacityGuard>();
        services.AddSingleton<DataPipelineCascadingPersistenceWriter>();
        services.AddSingleton<IRetryBackoffStrategy, DefaultRetryBackoffStrategy>();
        services.AddSingleton<IDataPipelineDeadLetterWriter, DataPipelineDeadLetterWriter>();
        services.AddSingleton<IDataPipelineConsumerInvoker, DefaultDataPipelineConsumerInvoker>();
        services.AddSingleton<ICloudFallbackRecoveryService, CloudFallbackRecoveryService>();
        services.AddSingleton<ICloudRetryRecordProcessor, CloudRetryRecordProcessor>();
        services.AddSingleton<ICloudRetryHousekeepingService, CloudRetryHousekeepingService>();
        services.AddSingleton<IMesFallbackRecoveryService, MesFallbackRecoveryService>();
        services.AddSingleton<IMesRetryRecordProcessor, MesRetryRecordProcessor>();
        services.AddSingleton<IMesRetryHousekeepingService, MesRetryHousekeepingService>();
        services.AddSingleton(sp => new DataPipelineService(
            sp.GetRequiredService<ILogService>(),
            sp.GetRequiredService<IDataPipelineIngressStore>(),
            sp.GetRequiredService<IDevicePluginRuntimeContext>()));
        services.AddSingleton<IDataPipelineService>(sp => sp.GetRequiredService<DataPipelineService>());

        services.AddSingleton<ICellDataConsumer>(sp => sp.GetRequiredService<ICapacityConsumer>());
        services.AddSingleton<ICellDataConsumer>(sp => sp.GetRequiredService<IMesConsumer>());
        services.AddSingleton<ICellDataConsumer>(sp => sp.GetRequiredService<ICloudConsumer>());

        services.AddSingleton<IUiNotifyConsumer, UiNotifyConsumer>();
        services.AddSingleton<ICellDataConsumer>(sp => sp.GetRequiredService<IUiNotifyConsumer>());

        services.AddSingleton<ProcessQueueTask>();
        services.AddSingleton<CloudRetryTask>();
        services.AddSingleton<MesRetryTask>();
    }
}
