namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 一个插件模块参数的原始字符串快照。
/// </summary>
public sealed record ModuleParamValueSnapshot(
    string ModuleId,
    IReadOnlyDictionary<string, string> Values);
