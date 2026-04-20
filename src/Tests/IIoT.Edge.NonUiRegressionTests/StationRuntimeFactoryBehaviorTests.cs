using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.Injection.Runtime;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Runtime;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class StationRuntimeFactoryBehaviorTests
{
    [Fact]
    public void InjectionFactory_WhenNoConfirmedTasksExist_ShouldReturnEmptyBaselineList()
    {
        var factory = new InjectionStationRuntimeFactory();

        var tasks = factory.CreateTasks(
            serviceProvider: new ServiceCollection().BuildServiceProvider(),
            buffer: new PlcBuffer(16, 16),
            context: new ProductionContext { DeviceName = "PLC-A" });

        Assert.Equal("Injection", factory.ModuleId);
        Assert.Empty(tasks);
    }

    [Fact]
    public void StackingFactory_WhenFirstRuntimeSliceIsEnabled_ShouldReturnSignalCaptureTask()
    {
        var factory = new StackingStationRuntimeFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILogService, FakeLogService>();
        services.AddSingleton<IDataPipelineService, FakeDataPipelineService>();

        var tasks = factory.CreateTasks(
            serviceProvider: services.BuildServiceProvider(),
            buffer: new PlcBuffer(16, 16),
            context: new ProductionContext { DeviceName = "PLC-B" });

        Assert.Equal(StackingModuleConstants.ModuleId, factory.ModuleId);
        Assert.Single(tasks);
        Assert.IsType<StackingSignalCaptureTask>(tasks[0]);
    }
}
