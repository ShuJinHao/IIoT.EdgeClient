using IIoT.Edge.Application.Abstractions.Config;
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
using IIoT.Edge.Runtime.Plc;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Runtime;

public static class DependencyInjection
{
    public static IServiceCollection AddEdgeRuntime(this IServiceCollection services, EdgeRuntimePaths runtimePaths)
    {
        services.AddSingleton(sp =>
            new ProductionContextStore(
                sp.GetRequiredService<ILogService>(),
                sp.GetServices<IProductionContextFactory>(),
                runtimePaths.ContextDirectory));
        services.AddSingleton<IProductionContextStore>(sp => sp.GetRequiredService<ProductionContextStore>());
        services.AddSingleton<ITodayCapacityStore, TodayCapacityStore>();
        services.AddSingleton<IPlcSignalBlockPlanner, DefaultPlcSignalBlockPlanner>();

        AddDataPipelineRuntimeCore(services);

        return services;
    }

    private static void AddDataPipelineRuntimeCore(IServiceCollection services)
    {
        services.AddSingleton<DataPipelineCapacityGuard>();
        services.AddSingleton<DataPipelineCascadingPersistenceWriter>();
        services.AddSingleton<IIngressOverflowPersistence, IngressOverflowPersistence>();
        services.AddSingleton<IRetryBackoffStrategy, DefaultRetryBackoffStrategy>();
        services.AddSingleton<IDataPipelineDeadLetterWriter, DataPipelineDeadLetterWriter>();
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
