using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Runtime.Context;
using IIoT.Edge.Runtime.DataPipeline.Consumers;
using IIoT.Edge.Runtime.DataPipeline.Services;
using IIoT.Edge.Runtime.DataPipeline.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Runtime;

public static class DependencyInjection
{
    public static IServiceCollection AddEdgeRuntime(this IServiceCollection services)
    {
        services.AddSingleton<ProductionContextStore>();
        services.AddSingleton<IProductionContextStore>(sp => sp.GetRequiredService<ProductionContextStore>());
        services.AddSingleton<ITodayCapacityStore, TodayCapacityStore>();

        AddDataPipelineRuntimeCore(services);

        return services;
    }

    private static void AddDataPipelineRuntimeCore(IServiceCollection services)
    {
        services.AddSingleton<DataPipelineService>();
        services.AddSingleton<IDataPipelineService>(sp => sp.GetRequiredService<DataPipelineService>());

        services.AddSingleton<ICellDataConsumer>(sp => sp.GetRequiredService<ICapacityConsumer>());
        services.AddSingleton<ICellDataConsumer>(sp => sp.GetRequiredService<IMesConsumer>());
        services.AddSingleton<ICellDataConsumer>(sp => sp.GetRequiredService<ICloudConsumer>());

        services.AddSingleton<IUiNotifyConsumer, UiNotifyConsumer>();
        services.AddSingleton<ICellDataConsumer>(sp => sp.GetRequiredService<IUiNotifyConsumer>());

        services.AddSingleton<ProcessQueueTask>();

        services.AddSingleton<RetryTask>(sp => new RetryTask(
            "Cloud",
            sp.GetRequiredService<ILogService>(),
            sp.GetRequiredService<IFailedRecordStore>(),
            sp.GetRequiredService<IDeviceService>(),
            sp.GetServices<ICellDataConsumer>(),
            sp.GetRequiredService<IDeviceLogSyncTask>(),
            sp.GetRequiredService<ICapacitySyncTask>(),
            sp.GetService<ICloudBatchConsumer>(),
            sp.GetRequiredService<ICloudFallbackBufferStore>(),
            sp.GetRequiredService<IMesFallbackBufferStore>(),
            sp.GetService<IProcessIntegrationRegistry>()));

        services.AddSingleton<RetryTask>(sp => new RetryTask(
            "MES",
            sp.GetRequiredService<ILogService>(),
            sp.GetRequiredService<IFailedRecordStore>(),
            sp.GetRequiredService<IDeviceService>(),
            sp.GetServices<ICellDataConsumer>(),
            mesFallbackStore: sp.GetRequiredService<IMesFallbackBufferStore>()));
    }
}
