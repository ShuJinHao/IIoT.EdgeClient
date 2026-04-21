namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 本地系统参数只读快照。
/// </summary>
public sealed record LocalSystemConfigSnapshot(
    int Id,
    string Key,
    string Value,
    string? Description,
    int SortOrder);
