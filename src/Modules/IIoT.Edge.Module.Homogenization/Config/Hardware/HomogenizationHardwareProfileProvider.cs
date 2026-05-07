using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Homogenization.Config.Hardware;

/// <summary>
/// 匀浆硬件模板提供者，聚合交互、单点读、连续读、单点写、连续写五类插件点位并转换为宿主可导入的 IO 模板。
/// </summary>
public sealed class HomogenizationHardwareProfileProvider : IModuleHardwareProfileProvider
{
    private readonly IModulePlcSignalProfile<HomogenizationPlcSignals.Interaction> _interactionProfile;
    private readonly IModulePlcSignalProfile<HomogenizationPlcSignals.SingleRead> _singleReadProfile;
    private readonly IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousRead> _continuousReadProfile;
    private readonly IModulePlcSignalProfile<HomogenizationPlcSignals.SingleWrite> _singleWriteProfile;
    private readonly IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousWrite> _continuousWriteProfile;

    /// <summary>
    /// 使用插件默认五类 profile 创建硬件模板提供者，主要用于测试和开发样本。
    /// </summary>
    public HomogenizationHardwareProfileProvider()
        : this(
            new HomogenizationInteractionSignalProfile(),
            new HomogenizationSingleReadSignalProfile(),
            new HomogenizationContinuousReadSignalProfile(),
            new HomogenizationSingleWriteSignalProfile(),
            new HomogenizationContinuousWriteSignalProfile())
    {
    }

    /// <summary>
    /// 使用宿主 DI 注入的五类 profile 创建硬件模板提供者。
    /// </summary>
    public HomogenizationHardwareProfileProvider(
        IModulePlcSignalProfile<HomogenizationPlcSignals.Interaction> interactionProfile,
        IModulePlcSignalProfile<HomogenizationPlcSignals.SingleRead> singleReadProfile,
        IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousRead> continuousReadProfile,
        IModulePlcSignalProfile<HomogenizationPlcSignals.SingleWrite> singleWriteProfile,
        IModulePlcSignalProfile<HomogenizationPlcSignals.ContinuousWrite> continuousWriteProfile)
    {
        _interactionProfile = interactionProfile ?? throw new ArgumentNullException(nameof(interactionProfile));
        _singleReadProfile = singleReadProfile ?? throw new ArgumentNullException(nameof(singleReadProfile));
        _continuousReadProfile = continuousReadProfile ?? throw new ArgumentNullException(nameof(continuousReadProfile));
        _singleWriteProfile = singleWriteProfile ?? throw new ArgumentNullException(nameof(singleWriteProfile));
        _continuousWriteProfile = continuousWriteProfile ?? throw new ArgumentNullException(nameof(continuousWriteProfile));
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
    /// 输出带特性的完整默认模板，只用于首次播种和手动重置标准点位。
    /// </summary>
    public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate()
        => AllSignals()
            .OrderBy(static signal => signal.SortOrder)
            .Select(CreateTemplateEntry)
            .ToArray();

    /// <summary>
    /// 输出新增 IO 下拉候选。这里遍历匀浆全量业务枚举；没有特性的成员也允许现场手工填写地址。
    /// </summary>
    public IReadOnlyList<ModuleIoTemplateEntry> GetIoMappingCandidates()
        => AllCandidateSignals()
            .OrderBy(static signal => signal.SortOrder)
            .ThenBy(static signal => signal.SignalKey, StringComparer.OrdinalIgnoreCase)
            .Select(CreateTemplateEntry)
            .ToArray();

    /// <summary>
    /// 校验当前 PLC 已配置映射的形态。标准点位只是播种模板，用户删除整套业务动作后不再按缺失点位报错。
    /// </summary>
    public ModuleHardwareValidationResult ValidatePlcConfiguration(
        string deviceName,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
        => ModuleHardwareProfileValidator.Validate(
            deviceName,
            mappings,
            CreateRequirementsForExistingMappings(mappings),
            requireCategory: true,
            validateSequentialOrder: false);

    private IReadOnlyCollection<ModuleHardwareSignalRequirement> CreateRequirementsForExistingMappings(
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
    {
        var existingKeys = mappings
            .Select(static mapping => CreateDirectionSignalKey(mapping.SignalKey, mapping.Direction))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return AllSignals()
            .Where(signal => existingKeys.Contains(CreateDirectionSignalKey(signal.SignalKey, signal.DirectionText)))
            .Select(static signal => new ModuleHardwareSignalRequirement(
                    signal.SignalKey,
                    signal.AddressCount,
                    signal.DataType,
                    signal.DirectionText,
                    signal.SortOrder,
                    signal.Category))
            .ToArray();
    }

    private static string CreateDirectionSignalKey(string signalKey, string direction)
        => $"{direction.Trim().ToUpperInvariant()}:{signalKey.Trim().ToUpperInvariant()}";

    private IEnumerable<SignalTemplate> AllSignals()
        =>
        [
            .. _interactionProfile.Signals.Select(SignalTemplate.From),
            .. _singleReadProfile.Signals.Select(SignalTemplate.From),
            .. _continuousReadProfile.Signals.Select(SignalTemplate.From),
            .. _singleWriteProfile.Signals.Select(SignalTemplate.From),
            .. _continuousWriteProfile.Signals.Select(SignalTemplate.From)
        ];

    private static IEnumerable<SignalTemplate> AllCandidateSignals()
        =>
        [
            .. Enum.GetValues<HomogenizationPlcSignals.Interaction>().SelectMany(CreateInteractionCandidates),
            .. Enum.GetValues<HomogenizationPlcSignals.SingleRead>().Select(CreateSingleReadCandidate),
            .. Enum.GetValues<HomogenizationPlcSignals.ContinuousRead>().Select(CreateContinuousReadCandidate),
            .. Enum.GetValues<HomogenizationPlcSignals.SingleWrite>().Select(CreateSingleWriteCandidate),
            .. Enum.GetValues<HomogenizationPlcSignals.ContinuousWrite>().Select(CreateContinuousWriteCandidate)
        ];

    private static IReadOnlyList<SignalTemplate> CreateInteractionCandidates(HomogenizationPlcSignals.Interaction signal)
    {
        var metadata = HomogenizationSignalMetadata.TryGetInteractionMetadata(signal);
        var signalKey = metadata?.SignalKey ?? CreateFallbackSignalKey("Homogenization.Interaction", signal);
        var businessGroup = metadata?.BusinessGroup ?? signal.ToString();
        var addressCount = IoMappingOptionCatalog.NormalizeAddressCount(
            IoMappingOptionCatalog.CategoryInteraction,
            metadata?.AddressCount ?? 1);
        var dataType = metadata?.DataType ?? IoMappingOptionCatalog.DataTypeInt16;

        return
        [
            new SignalTemplate(
                signalKey,
                $"{businessGroup} PLC 读点",
                metadata?.ReadAddress ?? string.Empty,
                addressCount,
                dataType,
                IoMappingOptionCatalog.DirectionRead,
                metadata?.ReadSortOrder ?? 10000 + Convert.ToInt32(signal),
                IoMappingOptionCatalog.CategoryInteraction,
                businessGroup,
                metadata?.ReadSignalName ?? "PLC 触发"),
            new SignalTemplate(
                signalKey,
                $"{businessGroup} 上位机写点",
                metadata?.WriteAddress ?? string.Empty,
                addressCount,
                dataType,
                IoMappingOptionCatalog.DirectionWrite,
                metadata?.WriteSortOrder ?? 20000 + Convert.ToInt32(signal),
                IoMappingOptionCatalog.CategoryInteraction,
                businessGroup,
                metadata?.WriteSignalName ?? "上位机应答")
        ];
    }

    private static SignalTemplate CreateSingleReadCandidate(HomogenizationPlcSignals.SingleRead signal)
        => CreateReadCandidate(signal, IoMappingOptionCatalog.CategorySingleRead, 30000);

    private static SignalTemplate CreateContinuousReadCandidate(HomogenizationPlcSignals.ContinuousRead signal)
        => CreateReadCandidate(signal, IoMappingOptionCatalog.CategoryContinuousRead, 40000);

    private static SignalTemplate CreateSingleWriteCandidate(HomogenizationPlcSignals.SingleWrite signal)
        => CreateWriteCandidate(signal, IoMappingOptionCatalog.CategorySingleWrite, 50000);

    private static SignalTemplate CreateContinuousWriteCandidate(HomogenizationPlcSignals.ContinuousWrite signal)
        => CreateWriteCandidate(signal, IoMappingOptionCatalog.CategoryContinuousWrite, 60000);

    private static SignalTemplate CreateReadCandidate<TSignal>(TSignal signal, string category, int sortOrderBase)
        where TSignal : struct, Enum
    {
        var metadata = HomogenizationSignalMetadata.TryGetReadMetadata(signal);
        var signalName = metadata?.SignalName ?? signal.ToString();
        var businessGroup = metadata?.BusinessGroup ?? signal.ToString();

        return new SignalTemplate(
            metadata?.SignalKey ?? CreateFallbackSignalKey(CreateFallbackPrefix(category), signal),
            metadata?.DisplayName ?? signalName,
            metadata?.DefaultAddress ?? string.Empty,
            IoMappingOptionCatalog.NormalizeAddressCount(category, metadata?.AddressCount ?? 1),
            metadata?.DataType ?? IoMappingOptionCatalog.DataTypeInt16,
            IoMappingOptionCatalog.DirectionRead,
            metadata?.SortOrder ?? sortOrderBase + Convert.ToInt32(signal),
            category,
            businessGroup,
            signalName);
    }

    private static SignalTemplate CreateWriteCandidate<TSignal>(TSignal signal, string category, int sortOrderBase)
        where TSignal : struct, Enum
    {
        var metadata = HomogenizationSignalMetadata.TryGetWriteMetadata(signal);
        var signalName = metadata?.SignalName ?? signal.ToString();
        var businessGroup = metadata?.BusinessGroup ?? signal.ToString();

        return new SignalTemplate(
            metadata?.SignalKey ?? CreateFallbackSignalKey(CreateFallbackPrefix(category), signal),
            metadata?.DisplayName ?? signalName,
            metadata?.DefaultAddress ?? string.Empty,
            IoMappingOptionCatalog.NormalizeAddressCount(category, metadata?.AddressCount ?? 1),
            metadata?.DataType ?? IoMappingOptionCatalog.DataTypeInt16,
            IoMappingOptionCatalog.DirectionWrite,
            metadata?.SortOrder ?? sortOrderBase + Convert.ToInt32(signal),
            category,
            businessGroup,
            signalName);
    }

    private static string CreateFallbackPrefix(string category)
        => category switch
        {
            IoMappingOptionCatalog.CategorySingleRead => "Homogenization.SingleRead",
            IoMappingOptionCatalog.CategoryContinuousRead => "Homogenization.ContinuousRead",
            IoMappingOptionCatalog.CategorySingleWrite => "Homogenization.SingleWrite",
            IoMappingOptionCatalog.CategoryContinuousWrite => "Homogenization.ContinuousWrite",
            _ => "Homogenization.Io"
        };

    private static string CreateFallbackSignalKey<TSignal>(string prefix, TSignal signal)
        where TSignal : struct, Enum
        => $"{prefix}.{signal}";

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
