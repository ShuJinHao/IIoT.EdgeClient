using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config.UseCases.DeviceParam.Queries;
using IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Queries;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.SharedKernel.Enums;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Application.Features.Config.LocalParameterConfig;

/// <summary>
/// 统一封装本地系统参数和设备参数的读取与变更通知。
/// 内部复用现有查询与缓存语义，不直接改写底层存储模型。
/// </summary>
public sealed class LocalParameterConfigService(
    IServiceScopeFactory scopeFactory)
    : ILocalParameterConfigService, ILocalParameterConfigChangePublisher
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged;

    public async Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var result = await sender.Send(new GetAllSystemConfigsQuery(), cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return [];
        }

        return result.Value
            .OrderBy(x => x.SortOrder)
            .Select(MapSystemConfig)
            .ToList();
    }

    public async Task<string?> GetSystemConfigValueAsync(
        SystemConfigKey key,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var result = await sender.Send(new GetSystemConfigValueQuery(key), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<IReadOnlyList<LocalDeviceParameterSnapshot>> GetDeviceParamsAsync(
        int deviceId,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var result = await sender.Send(new GetDeviceParamsQuery(deviceId), cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return [];
        }

        return result.Value
            .OrderBy(x => x.SortOrder)
            .Select(MapDeviceParam)
            .ToList();
    }

    public async Task<string?> GetDeviceParamValueAsync(
        int deviceId,
        DeviceParamKey key,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var result = await sender.Send(new GetDeviceParamValueQuery(deviceId, key), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    public void NotifySystemChanged()
        => ParameterConfigChanged?.Invoke(
            this,
            new ParameterConfigChangedEventArgs(ParameterConfigChangeScope.System));

    public void NotifyDeviceChanged(int deviceId)
        => ParameterConfigChanged?.Invoke(
            this,
            new ParameterConfigChangedEventArgs(
                ParameterConfigChangeScope.Device,
                deviceId));

    private static LocalSystemConfigSnapshot MapSystemConfig(SystemConfigEntity entity)
        => new(
            entity.Id,
            entity.Key,
            entity.Value,
            entity.Description,
            entity.SortOrder);

    private static LocalDeviceParameterSnapshot MapDeviceParam(DeviceParamEntity entity)
        => new(
            entity.Id,
            entity.NetworkDeviceId,
            entity.Name,
            entity.Value,
            entity.Unit,
            entity.MinValue,
            entity.MaxValue,
            entity.SortOrder);
}
