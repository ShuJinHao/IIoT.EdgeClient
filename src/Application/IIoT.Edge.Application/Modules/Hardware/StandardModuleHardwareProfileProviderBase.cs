using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;

namespace IIoT.Edge.Application.Modules.Hardware;

/// <summary>
/// 标准 PLC 插件硬件模板提供者，聚合交互、单点读、连续读、单点写、连续写五类枚举 profile。
/// </summary>
public abstract class StandardModuleHardwareProfileProviderBase<TInteraction, TSingleRead, TContinuousRead, TSingleWrite, TContinuousWrite>
    : ModuleHardwareProfileProviderBase
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
    private readonly Lazy<IReadOnlyList<ModuleHardwareSignalTemplate>> _templateSignals;

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

        _templateSignals = new Lazy<IReadOnlyList<ModuleHardwareSignalTemplate>>(BuildAllSignals);
    }

    public override string ModuleId => _interactionProfile.ModuleId;

    protected abstract string ModuleDisplayName { get; }

    protected override IReadOnlyList<ModuleHardwareSignalTemplate> TemplateSignals => _templateSignals.Value;

    protected override bool RequireCategory => true;

    protected override IEnumerable<ModuleHardwareSignalTemplate> GetDefaultTemplateSignals()
        => TemplateSignals
            .Where(static signal => !string.IsNullOrWhiteSpace(signal.DefaultAddress))
            .OrderBy(static signal => signal.SortOrder);

    protected override IEnumerable<ModuleHardwareSignalTemplate> GetIoMappingCandidateSignals()
        => TemplateSignals
            .OrderBy(static signal => signal.SortOrder)
            .ThenBy(static signal => signal.SignalKey, StringComparer.OrdinalIgnoreCase);

    protected override IReadOnlyCollection<ModuleHardwareSignalRequirement> CreateValidationRequirements(
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
    {
        var existingKeys = mappings
            .Select(static mapping => CreateDirectionSignalKey(mapping.SignalKey, mapping.Direction))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return TemplateSignals
            .Where(signal => existingKeys.Contains(CreateDirectionSignalKey(signal.SignalKey, signal.Direction)))
            .Select(static signal => new ModuleHardwareSignalRequirement(
                    signal.SignalKey,
                    signal.AddressCount,
                    signal.DataType,
                    signal.Direction,
                    signal.SortOrder,
                    signal.Category))
            .ToArray();
    }

    protected override string CreateTemplateRemark(ModuleHardwareSignalTemplate signal)
        => $"{ModuleDisplayName} - {signal.DisplayName}";

    private IReadOnlyList<ModuleHardwareSignalTemplate> BuildAllSignals()
    {
        var signals = new[]
            {
                _interactionProfile.Signals.Select(ModuleHardwareSignalTemplate.From),
                _singleReadProfile.Signals.Select(ModuleHardwareSignalTemplate.From),
                _continuousReadProfile.Signals.Select(ModuleHardwareSignalTemplate.From),
                _singleWriteProfile.Signals.Select(ModuleHardwareSignalTemplate.From),
                _continuousWriteProfile.Signals.Select(ModuleHardwareSignalTemplate.From)
            }
            .SelectMany(static signal => signal)
            .ToArray();

        EnsureUniqueDirectionSignalKeys(signals);
        return signals;
    }

    private void EnsureUniqueDirectionSignalKeys(IReadOnlyCollection<ModuleHardwareSignalTemplate> signals)
    {
        var duplicate = signals
            .GroupBy(static signal => CreateDirectionSignalKey(signal.SignalKey, signal.Direction))
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"模块【{ModuleId}】PLC 信号存在重复 SignalKey/方向：{duplicate.Key}");
        }
    }

    private static void EnsureSameModuleId(string expectedModuleId, string actualModuleId, string profileName)
    {
        if (!string.Equals(expectedModuleId, actualModuleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"标准硬件模板 profile【{profileName}】的 ModuleId【{actualModuleId}】与交互 profile【{expectedModuleId}】不一致。");
        }
    }

}
