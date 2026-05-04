namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 宿主按标准角色读取到的插件参数值。
/// </summary>
public sealed record ModuleParamRoleValue(
    string ModuleId,
    ModuleParamCategory Category,
    ModuleParamRole Role,
    ParamValueKind ValueKind,
    string Name,
    string StorageKey,
    string Value,
    string? DefaultValue);
