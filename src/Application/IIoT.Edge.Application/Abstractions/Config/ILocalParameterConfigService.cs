using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 客户端本地参数统一读取入口。
/// 只负责本地系统参数和设备参数，不包含配方。
/// </summary>
public interface ILocalParameterConfigService
{
    event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged;

    Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
        CancellationToken cancellationToken = default);

    Task<string?> GetSystemConfigValueAsync(
        SystemConfigKey key,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalDeviceParameterSnapshot>> GetDeviceParamsAsync(
        int deviceId,
        CancellationToken cancellationToken = default);

    Task<string?> GetDeviceParamValueAsync(
        int deviceId,
        DeviceParamKey key,
        CancellationToken cancellationToken = default);
}
