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
        var valid = request.Params
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Name)
            .Select(g => g.Last())
            .ToList();

        await repo.ExecuteDeleteAsync(x => x.NetworkDeviceId == request.DeviceId);

        for (int i = 0; i < valid.Count; i++)
        {
            var p = valid[i];
            repo.Add(new DeviceParamEntity(
                request.DeviceId, p.Name, p.Value, p.Unit)
            {
                MinValue = p.MinValue,
                MaxValue = p.MaxValue,
                SortOrder = i + 1
            });
        }
        await repo.SaveChangesAsync(cancellationToken);

        cache.Remove(CachePrefix + request.DeviceId);
        changePublisher.NotifyDeviceChanged(request.DeviceId);
        return Result.Success();
    }
}
