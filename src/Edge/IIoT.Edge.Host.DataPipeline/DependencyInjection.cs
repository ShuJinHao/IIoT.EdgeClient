using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Host.DataPipeline.Context;
using IIoT.Edge.Host.DataPipeline.Consumers;
using IIoT.Edge.Host.DataPipeline.Services;
using IIoT.Edge.Host.DataPipeline.Tasks;
using IIoT.Edge.Module.Sdk.Signals;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using IIoT.Edge.Application.Abstractions.Mes;
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
        services.AddSingleton(sp =>
            new ProductionContextStore(
                sp.GetRequiredService<ILogService>(),
                sp.GetServices<IProductionContextFactory>(),
                sp.GetRequiredService<ICellDataTypeRegistry>(),
                new ProductionContextPersistenceFileSystem(),
                runtimePaths.ContextDirectory,
                sp.GetRequiredService<IProductionContextCorruptFileQuarantine>(),
                sp.GetRequiredService<IProductionContextRuntimeStateCopier>()));
        services.AddSingleton<IProductionContextStore>(sp => sp.GetRequiredService<ProductionContextStore>());
        services.AddSingleton<ITodayCapacityStore, TodayCapacityStore>();

        AddHostDataPipelineCore(services);

        return services;
    }

    private static void AddHostDataPipelineCore(IServiceCollection services)
    {
        services.AddSingleton<DataPipelineCapacityGuard>();
        services.AddSingleton<DataPipelineCascadingPersistenceWriter>();
        services.AddSingleton<IIngressOverflowPersistence, IngressOverflowPersistence>();
        services.AddSingleton<IRetryBackoffStrategy, DefaultRetryBackoffStrategy>();
        services.AddSingleton<IDataPipelineDeadLetterWriter, DataPipelineDeadLetterWriter>();
        services.AddSingleton<IDataPipelineConsumerInvoker, DefaultDataPipelineConsumerInvoker>();
        services.AddSingleton<ICloudFallbackRecoveryService, CloudFallbackRecoveryService>();
        services.AddSingleton<ICloudRetryRecordProcessor, CloudRetryRecordProcessor>();
        services.AddSingleton<ICloudRetryHousekeepingService, CloudRetryHousekeepingService>();
        services.AddSingleton<IMesFallbackRecoveryService, MesFallbackRecoveryService>();
        services.AddSingleton<IMesRetryRecordProcessor, MesRetryRecordProcessor>();
        services.AddSingleton<IMesRetryHousekeepingService, MesRetryHousekeepingService>();
        services.AddSingleton<DataPipelineService>();
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
