using IIoT.Edge.Application.Abstractions.Plc.Signals;

namespace IIoT.Edge.Application.Abstractions.Modules;

/// <summary>
/// 插件 PLC 信号 profile 契约。接口由宿主定义，插件只能实现并通过模块 builder 注册。
/// </summary>
/// <typeparam name="TSignalKey">插件声明的 PLC 信号枚举。</typeparam>
public interface IModulePlcSignalProfile<TSignalKey>
    where TSignalKey : struct, Enum
{
    /// <summary>
    /// 当前信号 profile 所属模块标识，必须与插件 ModuleId 一致。
    /// </summary>
    string ModuleId { get; }

    /// <summary>
    /// 按业务含义拆分的信号分组。
    /// </summary>
    IReadOnlyList<ModuleSignalGroup<TSignalKey>> Groups { get; }

    /// <summary>
    /// 全部信号汇总，作为硬件模板、开发播种和 Runtime 访问的统一出口。
    /// </summary>
    IReadOnlyList<ModuleSignalDefinition<TSignalKey>> Signals { get; }

    /// <summary>
    /// 按强类型信号键获取信号定义。
    /// </summary>
    ModuleSignalDefinition<TSignalKey> Get(TSignalKey key);

    /// <summary>
    /// 按强类型信号键和 PLC 方向获取信号定义；信号交互允许同一业务键同时拥有读点和写点。
    /// </summary>
    ModuleSignalDefinition<TSignalKey> Get(TSignalKey key, ModuleSignalDirection direction);
}
