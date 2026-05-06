using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Homogenization.Config.Hardware;

/// <summary>
/// 匀浆硬件模板提供者，聚合交互、单点读、连续读三类插件点位并转换为宿主可导入的 IO 模板。
/// </summary>
public sealed class HomogenizationHardwareProfileProvider : IModuleHardwareProfileProvider
{
    private readonly IModulePlcSignalProfile<HomogenizationPlcSignals.Interaction> _interactionProfile;
    private readonly IModulePlcSignalProfile<HomogenizationPlcSignals.SingleRead> _singleReadProfile;
    private readonly IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousRead> _continuousReadProfile;

    /// <summary>
    /// 使用插件默认三类 profile 创建硬件模板提供者，主要用于测试和开发样本。
    /// </summary>
    public HomogenizationHardwareProfileProvider()
        : this(
            new HomogenizationInteractionSignalProfile(),
            new HomogenizationSingleReadSignalProfile(),
            new HomogenizationContinuousReadSignalProfile())
    {
    }

    /// <summary>
    /// 使用宿主 DI 注入的三类 profile 创建硬件模板提供者。
    /// </summary>
    public HomogenizationHardwareProfileProvider(
        IModulePlcSignalProfile<HomogenizationPlcSignals.Interaction> interactionProfile,
        IModulePlcSignalProfile<HomogenizationPlcSignals.SingleRead> singleReadProfile,
        IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousRead> continuousReadProfile)
    {
        _interactionProfile = interactionProfile ?? throw new ArgumentNullException(nameof(interactionProfile));
        _singleReadProfile = singleReadProfile ?? throw new ArgumentNullException(nameof(singleReadProfile));
        _continuousReadProfile = continuousReadProfile ?? throw new ArgumentNullException(nameof(continuousReadProfile));
    }

    /// <summary>
    /// 匀浆模块标识。
    /// </summary>
    public string ModuleId => DependencyInjection.ModuleKey;

    /// <summary>
    /// 匀浆默认 PLC 连接参数，作为开发样本和新设备导入的基础值。
    /// </summary>
    public ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.Mc.ToString(), 3000, 6000);

    /// <summary>
    /// 匀浆信号交互按连续 D 区块批量读写，允许少量地址空洞以减少 PLC 往返次数。
    /// </summary>
    public PlcIoRuntimePolicy GetIoRuntimePolicy()
        => new(
            SignalLoopIntervalMs: 10,
            MaxSignalBlockWordCount: 100,
            WriteGapPolicy: PlcIoWriteGapPolicy.Zero);

    /// <summary>
    /// 输出三类点位的完整默认模板，排序沿用插件 profile 的全局 SortOrder。
    /// </summary>
    public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate()
        => AllSignals()
            .OrderBy(static signal => signal.SortOrder)
            .Select(CreateTemplateEntry)
            .ToArray();

    /// <summary>
    /// 校验当前 PLC 映射是否覆盖匀浆插件声明的全部标准点位，并要求保留 IO 分类。
    /// </summary>
    public ModuleHardwareValidationResult ValidatePlcConfiguration(
        string deviceName,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
        => ModuleHardwareProfileValidator.Validate(
            deviceName,
            mappings,
            AllSignals()
                .Select(static signal => new ModuleHardwareSignalRequirement(
                    signal.SignalKey,
                    signal.AddressCount,
                    signal.DataType,
                    signal.DirectionText,
                    signal.SortOrder))
                .ToArray(),
            requireCategory: true,
            validateSequentialOrder: false);

    private IEnumerable<SignalTemplate> AllSignals()
        =>
        [
            .. _interactionProfile.Signals.Select(SignalTemplate.From),
            .. _singleReadProfile.Signals.Select(SignalTemplate.From),
            .. _continuousReadProfile.Signals.Select(SignalTemplate.From)
        ];

    private static ModuleIoTemplateEntry CreateTemplateEntry(SignalTemplate signal)
        => new(
            signal.SignalKey,
            signal.DefaultAddress,
            signal.AddressCount,
            signal.DataType,
            signal.DirectionText,
            signal.SortOrder,
            $"匀浆模块 - {signal.DisplayName}",
            signal.Category,
            signal.BusinessGroup,
            signal.SignalName);

    private sealed record SignalTemplate(
        string SignalKey,
        string DisplayName,
        string DefaultAddress,
        int AddressCount,
        string DataType,
        string DirectionText,
        int SortOrder,
        string Category,
        string BusinessGroup,
        string SignalName)
    {
        public static SignalTemplate From<TSignalKey>(ModuleSignalDefinition<TSignalKey> signal)
            where TSignalKey : struct, Enum
            => new(
                signal.SignalKey,
                signal.DisplayName,
                signal.DefaultAddress,
                signal.AddressCount,
                signal.DataType,
                signal.DirectionText,
                signal.SortOrder,
                signal.Category,
                signal.BusinessGroup,
                signal.SignalName);
    }
}
