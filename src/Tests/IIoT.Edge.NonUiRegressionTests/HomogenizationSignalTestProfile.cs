using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Homogenization.Config;

namespace IIoT.Edge.NonUiRegressionTests;

/// <summary>
/// 测试侧统一通过匀浆五类信号 profile 实例读取点位定义，避免回到生产代码里的静态信号链路。
/// </summary>
internal static class HomogenizationSignalTestProfile
{
    private const string ModuleId = "Homogenization";

    private static readonly EnumInteractionSignalProfile<HomogenizationPlcSignals.Interaction> InteractionProfile = new(ModuleId);
    private static readonly EnumReadSignalProfile<HomogenizationPlcSignals.SingleRead> SingleReadProfile = new(ModuleId, IoMappingOptionCatalog.CategorySingleRead);
    private static readonly EnumReadSignalProfile<HomogenizationPlcSignals.ContinuousRead> ContinuousReadProfile = new(ModuleId, IoMappingOptionCatalog.CategoryContinuousRead);
    private static readonly EnumWriteSignalProfile<HomogenizationPlcSignals.SingleWrite> SingleWriteProfile = new(ModuleId, IoMappingOptionCatalog.CategorySingleWrite);
    private static readonly EnumWriteSignalProfile<HomogenizationPlcSignals.ContinuousWrite> ContinuousWriteProfile = new(ModuleId, IoMappingOptionCatalog.CategoryContinuousWrite);

    public static IModulePlcSignalProfile<HomogenizationPlcSignals.Interaction> InteractionProfileInstance => InteractionProfile;

    public static IModulePlcSignalProfile<HomogenizationPlcSignals.SingleRead> SingleReadProfileInstance => SingleReadProfile;

    public static IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousRead> ContinuousReadProfileInstance => ContinuousReadProfile;

    public static IReadOnlyList<HomogenizationTestSignalDefinition> Signals { get; } =
    [
        .. InteractionProfile.Signals.Select(HomogenizationTestSignalDefinition.From)
            .Concat(SingleReadProfile.Signals.Select(HomogenizationTestSignalDefinition.From))
            .Concat(ContinuousReadProfile.Signals.Select(HomogenizationTestSignalDefinition.From))
            .Concat(SingleWriteProfile.Signals.Select(HomogenizationTestSignalDefinition.From))
            .Concat(ContinuousWriteProfile.Signals.Select(HomogenizationTestSignalDefinition.From))
            .OrderBy(static signal => signal.SortOrder)
    ];

    public static IReadOnlyList<HomogenizationTestSignalDefinition> Group(string name)
        =>
        [
            .. InteractionProfile.Groups.Where(group => group.Name == name).SelectMany(static group => group.Signals).Select(HomogenizationTestSignalDefinition.From),
            .. SingleReadProfile.Groups.Where(group => group.Name == name).SelectMany(static group => group.Signals).Select(HomogenizationTestSignalDefinition.From),
            .. ContinuousReadProfile.Groups.Where(group => group.Name == name).SelectMany(static group => group.Signals).Select(HomogenizationTestSignalDefinition.From),
            .. SingleWriteProfile.Groups.Where(group => group.Name == name).SelectMany(static group => group.Signals).Select(HomogenizationTestSignalDefinition.From),
            .. ContinuousWriteProfile.Groups.Where(group => group.Name == name).SelectMany(static group => group.Signals).Select(HomogenizationTestSignalDefinition.From)
        ];

    public static HomogenizationTestSignalDefinition Get(HomogenizationPlcSignals.Interaction key)
        => HomogenizationTestSignalDefinition.From(InteractionProfile.Get(key));

    public static HomogenizationTestSignalDefinition Get(HomogenizationPlcSignals.Interaction key, ModuleSignalDirection direction)
        => HomogenizationTestSignalDefinition.From(InteractionProfile.Get(key, direction));

    public static HomogenizationTestSignalDefinition Get(HomogenizationPlcSignals.SingleRead key)
        => HomogenizationTestSignalDefinition.From(SingleReadProfile.Get(key));

    public static HomogenizationTestSignalDefinition Get(HomogenizationPlcSignals.ContinuousRead key)
        => HomogenizationTestSignalDefinition.From(ContinuousReadProfile.Get(key));

    public static string SignalKey(HomogenizationPlcSignals.Interaction key)
        => Get(key).SignalKey;

    public static string SignalKey(HomogenizationPlcSignals.SingleRead key)
        => Get(key).SignalKey;

    public static string SignalKey(HomogenizationPlcSignals.ContinuousRead key)
        => Get(key).SignalKey;
}

internal sealed record HomogenizationTestSignalDefinition(
    string SignalKey,
    string DisplayName,
    string DefaultAddress,
    int AddressCount,
    string DataType,
    ModuleSignalDirection Direction,
    string DirectionText,
    int SortOrder,
    string Category,
    string BusinessGroup)
{
    public static HomogenizationTestSignalDefinition From<TSignalKey>(ModuleSignalDefinition<TSignalKey> signal)
        where TSignalKey : struct, Enum
        => new(
            signal.SignalKey,
            signal.DisplayName,
            signal.DefaultAddress,
            signal.AddressCount,
            signal.DataType,
            signal.Direction,
            signal.DirectionText,
            signal.SortOrder,
            signal.Category,
            signal.BusinessGroup);
}
