namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 本地设备参数只读快照。
/// </summary>
public sealed record LocalDeviceParameterSnapshot(
    int Id,
    int DeviceId,
    string Name,
    string Value,
    string? Unit,
    string? MinValue,
    string? MaxValue,
    int SortOrder);
