using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;

namespace IIoT.Edge.Application.Modules.Hardware;

/// <summary>
/// 标准 PLC 插件硬件模板提供者，聚合交互、单点读、连续读、单点写、连续写五类枚举 profile。
/// </summary>
public abstract class StandardModuleHardwareProfileProviderBase<TInteraction, TSingleRead, TContinuousRead, TSingleWrite, TContinuousWrite>
    : IModuleHardwareProfileProvider
    where TInteraction : struct, Enum
    where TSingleRead : struct, Enum
    where TContinuousRead : struct, Enum
    where TSingleWrite : struct, Enum
    where TContinuousWrite : struct, Enum
{
    private readonly IModulePlcSignalProfile<TInteraction> _interactionProfile;
    private readonly IModulePlcSignalProfile<TSingleRead> _singleReadProfile;
    private readonly IModulePlcSignalProfile<TContinuousRead> _continuousReadProfile;
    private readonly IModulePlcSignalProfile<TSingleWrite> _singleWriteProfile;
    private readonly IModulePlcSignalProfile<TContinuousWrite> _continuousWriteProfile;

    protected StandardModuleHardwareProfileProviderBase(
        IModulePlcSignalProfile<TInteraction> interactionProfile,
        IModulePlcSignalProfile<TSingleRead> singleReadProfile,
        IModulePlcSignalProfile<TContinuousRead> continuousReadProfile,
        IModulePlcSignalProfile<TSingleWrite> singleWriteProfile,
        IModulePlcSignalProfile<TContinuousWrite> continuousWriteProfile)
    {
        _interactionProfile = interactionProfile ?? throw new ArgumentNullException(nameof(interactionProfile));
        _singleReadProfile = singleReadProfile ?? throw new ArgumentNullException(nameof(singleReadProfile));
        _continuousReadProfile = continuousReadProfile ?? throw new ArgumentNullException(nameof(continuousReadProfile));
        _singleWriteProfile = singleWriteProfile ?? throw new ArgumentNullException(nameof(singleWriteProfile));
        _continuousWriteProfile = continuousWriteProfile ?? throw new ArgumentNullException(nameof(continuousWriteProfile));

        EnsureSameModuleId(_interactionProfile.ModuleId, _singleReadProfile.ModuleId, nameof(singleReadProfile));
        EnsureSameModuleId(_interactionProfile.ModuleId, _continuousReadProfile.ModuleId, nameof(continuousReadProfile));
        EnsureSameModuleId(_interactionProfile.ModuleId, _singleWriteProfile.ModuleId, nameof(singleWriteProfile));
        EnsureSameModuleId(_interactionProfile.ModuleId, _continuousWriteProfile.ModuleId, nameof(continuousWriteProfile));
    }

    public string ModuleId => _interactionProfile.ModuleId;

    protected abstract string ModuleDisplayName { get; }

    public abstract ModulePlcDefaults GetDefaultPlcSettings();

    public virtual PlcIoRuntimePolicy GetIoRuntimePolicy()
        => PlcIoRuntimePolicy.Default;

    public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate()
        => AllSignals()
            .Where(static signal => !string.IsNullOrWhiteSpace(signal.DefaultAddress))
            .OrderBy(static signal => signal.SortOrder)
            .Select(CreateTemplateEntry)
            .ToArray();

    public virtual IReadOnlyList<ModuleIoTemplateEntry> GetIoMappingCandidates()
        => AllSignals()
            .OrderBy(static signal => signal.SortOrder)
            .ThenBy(static signal => signal.SignalKey, StringComparer.OrdinalIgnoreCase)
            .Select(CreateTemplateEntry)
            .ToArray();

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

    private IEnumerable<SignalTemplate> AllSignals()
    {
        var signals = new[]
            {
                _interactionProfile.Signals.Select(SignalTemplate.From),
                _singleReadProfile.Signals.Select(SignalTemplate.From),
                _continuousReadProfile.Signals.Select(SignalTemplate.From),
                _singleWriteProfile.Signals.Select(SignalTemplate.From),
                _continuousWriteProfile.Signals.Select(SignalTemplate.From)
            }
            .SelectMany(static signal => signal)
            .ToArray();

        EnsureUniqueDirectionSignalKeys(signals);
        return signals;
    }

    private void EnsureUniqueDirectionSignalKeys(IReadOnlyCollection<SignalTemplate> signals)
    {
        var duplicate = signals
            .GroupBy(static signal => CreateDirectionSignalKey(signal.SignalKey, signal.DirectionText))
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"模块【{ModuleId}】PLC 信号存在重复 SignalKey/方向：{duplicate.Key}");
        }
    }

    private ModuleIoTemplateEntry CreateTemplateEntry(SignalTemplate signal)
        => new(
            signal.SignalKey,
            signal.DefaultAddress,
            signal.AddressCount,
            signal.DataType,
            signal.DirectionText,
            signal.SortOrder,
            $"{ModuleDisplayName} - {signal.DisplayName}",
            signal.Category,
            signal.BusinessGroup,
            signal.SignalName);

    private static void EnsureSameModuleId(string expectedModuleId, string actualModuleId, string profileName)
    {
        if (!string.Equals(expectedModuleId, actualModuleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"标准硬件模板 profile【{profileName}】的 ModuleId【{actualModuleId}】与交互 profile【{expectedModuleId}】不一致。");
        }
    }

    private static string CreateDirectionSignalKey(string signalKey, string direction)
        => $"{direction.Trim().ToUpperInvariant()}:{signalKey.Trim().ToUpperInvariant()}";

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
