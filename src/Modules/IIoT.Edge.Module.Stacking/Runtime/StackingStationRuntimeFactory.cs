using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Runtime.Signals;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.Stacking.Runtime;

public sealed class StackingStationRuntimeFactory : IStationRuntimeFactory
{
    public string ModuleId => StackingModuleConstants.ModuleId;

    public List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(context);

        var signalAccessor = BufferLogicalSignalAccessor.Create(
            buffer,
            context,
            StackingPlcSignalProfile.LogicalSignals);

        return
        [
            new StackingSignalCaptureTask(
                buffer,
                signalAccessor,
                context,
                serviceProvider.GetRequiredService<IDataPipelineService>(),
                serviceProvider.GetRequiredService<ILogService>())
        ];
    }
}
