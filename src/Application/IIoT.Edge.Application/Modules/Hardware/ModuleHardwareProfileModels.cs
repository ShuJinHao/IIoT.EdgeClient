using IIoT.Edge.Application.Abstractions.Plc.Signals;

namespace IIoT.Edge.Application.Modules.Hardware;

/// <summary>
/// 信号交互写入块遇到未配置地址时的处理方式。
/// </summary>
public enum PlcIoWriteGapPolicy
{
    /// <summary>
    /// 保持一个连续写入块，未配置地址按 0 写入。
    /// </summary>
    Zero,

    /// <summary>
    /// 遇到未配置地址时拆分写入块，避免写入空洞地址。
    /// </summary>
    Split
}

/// <summary>
/// 插件声明的 PLC IO 运行策略。地址和长度仍来自插件点位 profile 或数据库映射。
/// </summary>
public sealed record PlcIoRuntimePolicy(
    int SignalLoopIntervalMs = 10,
    int MaxSignalBlockWordCount = 100,
    PlcIoWriteGapPolicy WriteGapPolicy = PlcIoWriteGapPolicy.Zero,
    int DataReadLoopIntervalMs = 1000)
{
    public static PlcIoRuntimePolicy Default { get; } = new();

    public int NormalizeSignalLoopInterval()
        => SignalLoopIntervalMs <= 0 ? Default.SignalLoopIntervalMs : SignalLoopIntervalMs;

    public int NormalizeLoopInterval()
        => NormalizeSignalLoopInterval();

    public int NormalizeDataReadLoopInterval()
        => DataReadLoopIntervalMs <= 0 ? Default.DataReadLoopIntervalMs : DataReadLoopIntervalMs;

    public int NormalizeMaxBlockWordCount()
        => MaxSignalBlockWordCount <= 0 ? Default.MaxSignalBlockWordCount : MaxSignalBlockWordCount;
}

public sealed record ModulePlcDefaults(
    string? DeviceModel,
    int? ConnectTimeout,
    int? Port1 = null);

public sealed record ModuleHardwareSignalTemplate(
    string SignalKey,
    string DisplayName,
    string DefaultAddress,
    int AddressCount,
    string DataType,
    string Direction,
    int SortOrder,
    string Category,
    string BusinessGroup)
{
    public static ModuleHardwareSignalTemplate From<TSignalKey>(
        ModuleSignalDefinition<TSignalKey> signal)
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
            signal.BusinessGroup);
}

public sealed record ModuleIoTemplateEntry(
    string SignalKey,
    string PlcAddress,
    int AddressCount,
    string DataType,
    string Direction,
    int SortOrder,
    string? Remark = null,
    string Category = "单点读数据",
    string BusinessGroup = "");

public sealed record ModuleIoSnapshot(
    string SignalKey,
    string PlcAddress,
    int AddressCount,
    string DataType,
    string Direction,
    int SortOrder,
    string Category = "单点读数据",
    string BusinessGroup = "");

public sealed record ModuleHardwareValidationIssue(string Message);

public sealed class ModuleHardwareValidationResult
{
    private ModuleHardwareValidationResult(IReadOnlyList<ModuleHardwareValidationIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<ModuleHardwareValidationIssue> Issues { get; }

    public bool IsValid => Issues.Count == 0;

    public static ModuleHardwareValidationResult Success()
        => new([]);

    public static ModuleHardwareValidationResult Failure(IEnumerable<ModuleHardwareValidationIssue> issues)
        => new(issues.ToList().AsReadOnly());
}
