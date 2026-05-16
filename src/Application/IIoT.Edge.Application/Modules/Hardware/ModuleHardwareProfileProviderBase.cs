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
        string businessGroup,
        string signalName)
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
            businessGroup,
            signalName);

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
/// <typeparam name="TSignalKey">插件声明的 PLC 信号枚举。</typeparam>
public abstract class ModuleHardwareProfileProviderBase<TSignalKey> : IModuleHardwareProfileProvider
    where TSignalKey : struct, Enum
{
    private readonly IModulePlcSignalProfile<TSignalKey> _signalProfile;
    private readonly IModuleHardwareProfileValidator _hardwareProfileValidator;

    protected ModuleHardwareProfileProviderBase(
        IModulePlcSignalProfile<TSignalKey> signalProfile,
        IModuleHardwareProfileValidator hardwareProfileValidator)
    {
        _signalProfile = signalProfile ?? throw new ArgumentNullException(nameof(signalProfile));
        _hardwareProfileValidator = hardwareProfileValidator ?? throw new ArgumentNullException(nameof(hardwareProfileValidator));
    }

    public string ModuleId => _signalProfile.ModuleId;

    protected IReadOnlyList<ModuleSignalDefinition<TSignalKey>> Signals => _signalProfile.Signals;

    protected virtual bool RequireCategory => false;

    protected virtual bool ValidateSequentialOrder => false;

    public abstract ModulePlcDefaults GetDefaultPlcSettings();

    public virtual PlcIoRuntimePolicy GetIoRuntimePolicy()
        => PlcIoRuntimePolicy.Default;

    public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate()
        => Signals
            .Select(CreateTemplateEntry)
            .ToArray();

    public virtual IReadOnlyList<ModuleIoTemplateEntry> GetIoMappingCandidates()
        => GetDefaultIoTemplate();

    public ModuleHardwareValidationResult ValidatePlcConfiguration(
        string deviceName,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
        => _hardwareProfileValidator.Validate(
            deviceName,
            mappings,
            Signals.Select(static signal => new ModuleHardwareSignalRequirement(
                    signal.SignalKey,
                    signal.AddressCount,
                    signal.DataType,
                    signal.DirectionText,
                    signal.SortOrder,
                    signal.Category))
                .ToArray(),
            RequireCategory,
            ValidateSequentialOrder);

    protected abstract string CreateTemplateRemark(ModuleSignalDefinition<TSignalKey> signal);

    private ModuleIoTemplateEntry CreateTemplateEntry(ModuleSignalDefinition<TSignalKey> signal)
        => new(
            signal.SignalKey,
            signal.DefaultAddress,
            signal.AddressCount,
            signal.DataType,
            signal.DirectionText,
            signal.SortOrder,
            CreateTemplateRemark(signal),
            signal.Category,
            signal.BusinessGroup,
            signal.SignalName);
}
