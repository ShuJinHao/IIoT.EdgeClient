namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 宿主从插件枚举生成的参数定义。
/// </summary>
public sealed record ModuleParamDescriptor(
    string ModuleId,
    ModuleParamCategory Category,
    Type EnumType,
    string Name,
    string StorageKey,
    ParamValueKind ValueKind,
    string? DefaultValue,
    string? Unit,
    string? MinValue,
    string? MaxValue,
    ModuleParamRole Role,
    int SortOrder);

/// <summary>
/// 一个插件注册的三类参数枚举。
/// </summary>
public sealed record ModuleParamRegistration(
    string ModuleId,
    Type MesParamType,
    Type CloudParamType,
    Type BusinessParamType);
