using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Module.Homogenization.Config.Hardware;

namespace IIoT.Edge.NonUiRegressionTests;

/// <summary>
/// 测试侧统一通过匀浆信号 profile 实例读取点位定义，避免回到生产代码里的静态信号链路。
/// </summary>
internal static class HomogenizationSignalTestProfile
{
    private static readonly HomogenizationPlcSignalProfile Profile = new();

    public static IReadOnlyList<ModuleSignalDefinition<HomogenizationSignal>> Signals => Profile.Signals;

    public static IReadOnlyList<ModuleSignalDefinition<HomogenizationSignal>> Group(string name)
        => Profile.Groups.Single(group => group.Name == name).Signals;

    public static ModuleSignalDefinition<HomogenizationSignal> Get(HomogenizationSignal key)
        => Profile.Get(key);

    public static string Label(HomogenizationSignal key)
        => Get(key).Label;
}
