using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Domain.Config.Aggregates;

namespace IIoT.Edge.Application.Features.Config.UseCases.DeviceParam.Commands;

/// <summary>
/// 单条设备参数的数据传输对象。
/// </summary>
public record DeviceParamDto(
    string Name,
    string Value,
    string? Unit = null,
    string? MinValue = null,
    string? MaxValue = null
);

/// <summary>
/// 命令：保存设备参数，以全量覆盖方式更新指定设备的参数。
/// </summary>
public record SaveDeviceParamsCommand(
    int DeviceId,
    List<DeviceParamDto> Params
) : ICommand<Result>;

public class SaveDeviceParamsHandler(
    IRepository<DeviceParamEntity> repo,
    IEdgeCacheService cache,
    ILocalParameterConfigChangePublisher changePublisher
) : ICommandHandler<SaveDeviceParamsCommand, Result>
{
    private const string CachePrefix = "Config:DeviceParam:";

    public async Task<Result> Handle(
        SaveDeviceParamsCommand request,
        CancellationToken cancellationToken)
    {
        List<DeviceParamEntity> parameters;
        try
        {
            parameters = request.Params
                .GroupBy(x => x.Name?.Trim() ?? string.Empty)
                .Select(g => g.Last())
                .Select((dto, index) =>
                {
                    var entity = DeviceParamEntity.Create(
                        request.DeviceId,
                        dto.Name,
                        dto.Value,
                        dto.Unit);
                    entity.UpdateBounds(dto.MinValue, dto.MaxValue);
                    entity.UpdateSortOrder(index + 1);
                    return entity;
                })
                .ToList();
        }
        catch (ArgumentException ex)
        {
            return Result.Failure(ex.Message);
        }

        await repo.ExecuteDeleteAsync(x => x.NetworkDeviceId == request.DeviceId, cancellationToken);

        foreach (var parameter in parameters)
        {
            repo.Add(parameter);
        }

        await repo.SaveChangesAsync(cancellationToken);

        cache.Remove(CachePrefix + request.DeviceId);
        changePublisher.NotifyDeviceChanged(request.DeviceId);
        return Result.Success();
    }
}
