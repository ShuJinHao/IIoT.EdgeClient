using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.Injection.Runtime;
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
}
