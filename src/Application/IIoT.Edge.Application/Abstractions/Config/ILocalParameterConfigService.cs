namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 客户端本地参数统一读取入口。
/// 只负责模块参数底层存储读取，不包含设备参数和配方。
/// </summary>
public interface ILocalParameterConfigService
{
    event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged;

    Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
        CancellationToken cancellationToken = default);

    Task InsertSystemConfigAsync(
        string key,
        string value,
        string? description = null,
        int sortOrder = 0,
        CancellationToken cancellationToken = default);

    Task DeleteSystemConfigAsync(
        string key,
        CancellationToken cancellationToken = default);
}
