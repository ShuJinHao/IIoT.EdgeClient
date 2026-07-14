using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Tasks;
using IIoT.Edge.Application.Modules.Descriptors;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.TestPlugin;

public sealed class DependencyInjection : EdgeProcessModuleBase<TestPluginCellData>
{
    public const string ModuleKey = "TestPlugin";
    public const string DataViewId = ModuleKey + ".DataView";

    public override string ModuleId => ModuleKey;

    public override string DisplayName => "Test Plugin";

    protected override IStationRuntimeFactory CreateRuntimeFactory()
        => new TestPluginRuntimeFactory();

    protected override void ConfigureModuleServices(IEdgeProcessModuleBuilder builder)
    {
        builder.RegisterHardwareProfile<TestPluginHardwareProfileProvider>();
        builder.Services.AddSingleton<TestPluginLifecycleProbe>();
        builder.Services.AddSingleton<TestPluginLifecycleService>();
        builder.Services.AddSingleton<IManagedBackgroundService>(serviceProvider =>
            serviceProvider.GetRequiredService<TestPluginLifecycleService>());
    }

    protected override void RegisterModuleViews(IEdgeProcessModuleBuilder builder)
    {
        builder.RegisterRoute(DataViewId, typeof(TestPluginView), typeof(TestPluginViewModel));
        builder.RegisterMenu(new ModuleMenuDescriptor
        {
            Title = DisplayName,
            ViewId = DataViewId,
            Icon = "Shape",
            Order = 99
        });
    }
}

public sealed class TestPluginCellData : CellDataBase
{
    public override string ProcessType => DependencyInjection.ModuleKey;
}

public sealed class TestPluginView;

public sealed class TestPluginViewModel;
