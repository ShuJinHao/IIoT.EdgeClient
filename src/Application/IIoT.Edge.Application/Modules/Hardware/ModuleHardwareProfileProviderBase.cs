using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;

namespace IIoT.Edge.Application.Modules.Hardware;

/// <summary>
/// 插件 PLC 信号 profile 基类，统一处理分组汇总、重复校验和中文异常。
/// </summary>
/// <typeparam name="TSignalKey">插件声明的 PLC 信号枚举。</typeparam>
public abstract class ModulePlcSignalProfileBase<TSignalKey> : IModulePlcSignalProfile<TSignalKey>
    where TSignalKey : struct, Enum
{
    private readonly Lazy<IReadOnlyList<ModuleSignalGroup<TSignalKey>>> _groups;
    private readonly Lazy<IReadOnlyList<ModuleSignalDefinition<TSignalKey>>> _signals;
    private readonly Lazy<IReadOnlyDictionary<TSignalKey, ModuleSignalDefinition<TSignalKey>>> _signalsByKey;
    private readonly Lazy<IReadOnlyDictionary<(TSignalKey Key, ModuleSignalDirection Direction), ModuleSignalDefinition<TSignalKey>>> _signalsByKeyAndDirection;

    protected ModulePlcSignalProfileBase()
    {
        _groups = new Lazy<IReadOnlyList<ModuleSignalGroup<TSignalKey>>>(() => BuildGroups().ToArray());
        _signals = new Lazy<IReadOnlyList<ModuleSignalDefinition<TSignalKey>>>(BuildAllSignals);
        _signalsByKey = new Lazy<IReadOnlyDictionary<TSignalKey, ModuleSignalDefinition<TSignalKey>>>(() =>
            Signals.GroupBy(static signal => signal.Key)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.OrderBy(static signal => signal.Direction == ModuleSignalDirection.Read ? 0 : 1).First()));
        _signalsByKeyAndDirection = new Lazy<IReadOnlyDictionary<(TSignalKey Key, ModuleSignalDirection Direction), ModuleSignalDefinition<TSignalKey>>>(() =>
            Signals.ToDictionary(static signal => (signal.Key, signal.Direction)));
    }

    public abstract string ModuleId { get; }

    public IReadOnlyList<ModuleSignalGroup<TSignalKey>> Groups => _groups.Value;

    public IReadOnlyList<ModuleSignalDefinition<TSignalKey>> Signals => _signals.Value;

    public ModuleSignalDefinition<TSignalKey> Get(TSignalKey key)
        => _signalsByKey.Value.TryGetValue(key, out var signal)
            ? signal
            : throw new InvalidOperationException($"模块【{ModuleId}】未声明 PLC 信号：{key}");

    public ModuleSignalDefinition<TSignalKey> Get(TSignalKey key, ModuleSignalDirection direction)
        => _signalsByKeyAndDirection.Value.TryGetValue((key, direction), out var signal)
            ? signal
            : throw new InvalidOperationException($"模块【{ModuleId}】未声明 PLC 信号：{key} / {direction}");

    /// <summary>
    /// 插件按业务含义构建信号分组，禁止把所有点位堆成一个扁平清单。
    /// </summary>
    protected abstract IEnumerable<ModuleSignalGroup<TSignalKey>> BuildGroups();

    protected ModuleSignalGroup<TSignalKey> Group(
        string name,
        params ModuleSignalDefinition<TSignalKey>[] signals)
        => new(name, signals);

    protected ModuleSignalDefinition<TSignalKey> Signal(
        TSignalKey key,
        string signalKey,
        string defaultAddress,
        ModuleSignalDirection direction,
        int addressCount,
        string dataType,
        int sortOrder,
        string displayName,
        string category,
        string businessGroup)
        => new(
            key,
            signalKey,
            displayName,
            defaultAddress,
            addressCount,
            dataType,
            direction,
            sortOrder,
            category,
            businessGroup);

    private IReadOnlyList<ModuleSignalDefinition<TSignalKey>> BuildAllSignals()
    {
        var signals = Groups.SelectMany(static group => group.Signals)
            .OrderBy(static signal => signal.SortOrder)
            .ToArray();

        EnsureUniqueKeys(signals);
        EnsureUniqueSignalKeys(signals);
        EnsureUniqueSortOrders(signals);
        return signals;
    }

    private void EnsureUniqueKeys(IReadOnlyList<ModuleSignalDefinition<TSignalKey>> signals)
    {
        var duplicate = signals.GroupBy(static signal => (signal.Key, signal.Direction))
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"模块【{ModuleId}】PLC 信号存在重复 Key/方向：{duplicate.Key.Key} / {duplicate.Key.Direction}");
        }
    }

    private void EnsureUniqueSignalKeys(IReadOnlyList<ModuleSignalDefinition<TSignalKey>> signals)
    {
        var duplicate = signals
            .GroupBy(static signal => new
            {
                SignalKey = signal.SignalKey.Trim().ToUpperInvariant(),
                signal.Direction
            })
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"模块【{ModuleId}】PLC 信号存在重复 SignalKey/方向：{duplicate.Key.SignalKey} / {duplicate.Key.Direction}");
        }
    }

    private void EnsureUniqueSortOrders(IReadOnlyList<ModuleSignalDefinition<TSignalKey>> signals)
    {
        var duplicate = signals.GroupBy(static signal => signal.SortOrder)
            .FirstOrDefault(static group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"模块【{ModuleId}】PLC 信号存在重复排序：{duplicate.Key}");
        }
    }
}

/// <summary>
/// 硬件模板提供者基类，把插件强类型信号 profile 转换为宿主可保存的 IO 模板。
/// </summary>
public abstract class ModuleHardwareProfileProviderBase : IModuleHardwareProfileProvider
{
    public abstract string ModuleId { get; }

    protected abstract IReadOnlyList<ModuleHardwareSignalTemplate> TemplateSignals { get; }

    protected virtual bool RequireCategory => false;

    protected virtual bool ValidateSequentialOrder => false;

    public abstract ModulePlcDefaults GetDefaultPlcSettings();

    public virtual PlcIoRuntimePolicy GetIoRuntimePolicy()
        => PlcIoRuntimePolicy.Default;

    public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate()
        => GetDefaultTemplateSignals()
            .Select(CreateTemplateEntry)
            .ToArray();

    public virtual IReadOnlyList<ModuleIoTemplateEntry> GetIoMappingCandidates()
        => GetIoMappingCandidateSignals()
            .Select(CreateTemplateEntry)
            .ToArray();

    public virtual ModuleIoTemplateEntry ResolveIoTemplateForDevice(
        string deviceName,
        ModuleIoTemplateEntry template)
        => template;

    public ModuleHardwareValidationResult ValidatePlcConfiguration(
        string deviceName,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
        => ModuleHardwareProfileValidator.Validate(
            deviceName,
            mappings,
            CreateValidationRequirements(mappings),
            RequireCategory,
            ValidateSequentialOrder);

    protected virtual IEnumerable<ModuleHardwareSignalTemplate> GetDefaultTemplateSignals()
        => TemplateSignals;

    protected virtual IEnumerable<ModuleHardwareSignalTemplate> GetIoMappingCandidateSignals()
        => GetDefaultTemplateSignals();

    protected virtual IReadOnlyCollection<ModuleHardwareSignalRequirement> CreateValidationRequirements(
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
        => TemplateSignals
            .Select(CreateRequirement)
            .ToArray();

    protected abstract string CreateTemplateRemark(ModuleHardwareSignalTemplate signal);

    protected static string CreateDirectionSignalKey(string signalKey, string direction)
        => $"{direction.Trim().ToUpperInvariant()}:{signalKey.Trim().ToUpperInvariant()}";

    private ModuleIoTemplateEntry CreateTemplateEntry(ModuleHardwareSignalTemplate signal)
        => new(
            signal.SignalKey,
            signal.DefaultAddress,
            signal.AddressCount,
            signal.DataType,
            signal.Direction,
            signal.SortOrder,
            CreateTemplateRemark(signal),
            signal.Category,
            signal.BusinessGroup);

    private static ModuleHardwareSignalRequirement CreateRequirement(ModuleHardwareSignalTemplate signal)
        => new(
            signal.SignalKey,
            signal.AddressCount,
            signal.DataType,
            signal.Direction,
            signal.SortOrder,
            signal.Category);
}

/// <summary>
/// 硬件模板提供者基类，把插件强类型信号 profile 转换为宿主可保存的 IO 模板。
/// </summary>
/// <typeparam name="TSignalKey">插件声明的 PLC 信号枚举。</typeparam>
public abstract class ModuleHardwareProfileProviderBase<TSignalKey> : ModuleHardwareProfileProviderBase
    where TSignalKey : struct, Enum
{
    private readonly IModulePlcSignalProfile<TSignalKey> _signalProfile;
    private readonly Lazy<IReadOnlyList<ModuleHardwareSignalTemplate>> _templateSignals;

    protected ModuleHardwareProfileProviderBase(IModulePlcSignalProfile<TSignalKey> signalProfile)
    {
        _signalProfile = signalProfile ?? throw new ArgumentNullException(nameof(signalProfile));
        _templateSignals = new Lazy<IReadOnlyList<ModuleHardwareSignalTemplate>>(() =>
            _signalProfile.Signals.Select(ModuleHardwareSignalTemplate.From).ToArray());
    }

    public override string ModuleId => _signalProfile.ModuleId;

    protected IReadOnlyList<ModuleSignalDefinition<TSignalKey>> Signals => _signalProfile.Signals;

    protected override IReadOnlyList<ModuleHardwareSignalTemplate> TemplateSignals => _templateSignals.Value;

    protected abstract string CreateTemplateRemark(ModuleSignalDefinition<TSignalKey> signal);

    protected sealed override string CreateTemplateRemark(ModuleHardwareSignalTemplate signal)
    {
        var source = Signals.FirstOrDefault(candidate =>
            string.Equals(candidate.SignalKey, signal.SignalKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(candidate.DirectionText, signal.Direction, StringComparison.OrdinalIgnoreCase));

        return source is null
            ? string.Empty
            : CreateTemplateRemark(source);
    }
}
