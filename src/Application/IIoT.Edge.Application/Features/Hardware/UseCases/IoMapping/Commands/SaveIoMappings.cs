using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;

/// <summary>
/// 单条 IO 映射的数据传输对象。
/// </summary>
public record IoMappingDto(
    int Id,
    int NetworkDeviceId,
    string Label,
    string PlcAddress,
    int AddressCount,
    string DataType,
    string Direction,
    string Category,
    string GroupName,
    string DisplayRole,
    int SortOrder,
    string? Remark
);

/// <summary>
/// 命令：保存指定网络设备下的 IO 映射，按提交结果进行新增或更新。
/// </summary>
public record SaveIoMappingsCommand(
    int NetworkDeviceId,
    List<IoMappingDto> Mappings
) : ICommand<Result>;

/// <summary>
/// 处理器：保存指定网络设备的 IO 映射配置。
/// </summary>
public class SaveIoMappingsHandler(
    IRepository<IoMappingEntity> repo
) : ICommandHandler<SaveIoMappingsCommand, Result>
{
    public async Task<Result> Handle(
        SaveIoMappingsCommand request,
        CancellationToken cancellationToken)
    {
        var existingMappings = await repo.GetListAsync(
            x => x.NetworkDeviceId == request.NetworkDeviceId,
            cancellationToken);
        var existingById = existingMappings.ToDictionary(x => x.Id);
        var submittedIds = request.Mappings
            .Where(x => x.Id > 0)
            .Select(x => x.Id)
            .ToHashSet();

        foreach (var entity in existingMappings.Where(x => !submittedIds.Contains(x.Id)))
        {
            repo.Delete(entity);
        }

        foreach (var dto in request.Mappings)
        {
            if (dto.Id == 0)
            {
                var entity = new IoMappingEntity(
                    request.NetworkDeviceId, dto.Label, dto.PlcAddress,
                    dto.AddressCount, dto.DataType, dto.Direction,
                    Normalize(dto.Category, "单点读数据"), dto.GroupName ?? string.Empty, dto.DisplayRole ?? string.Empty)
                {
                    SortOrder = dto.SortOrder,
                    Remark = dto.Remark
                };
                repo.Add(entity);
            }
            else if (existingById.TryGetValue(dto.Id, out var entity))
            {
                entity.NetworkDeviceId = request.NetworkDeviceId;
                entity.Label = dto.Label;
                entity.PlcAddress = dto.PlcAddress;
                entity.AddressCount = dto.AddressCount;
                entity.DataType = dto.DataType;
                entity.Direction = dto.Direction;
                entity.Category = Normalize(dto.Category, "单点读数据");
                entity.GroupName = dto.GroupName ?? string.Empty;
                entity.DisplayRole = dto.DisplayRole ?? string.Empty;
                entity.SortOrder = dto.SortOrder;
                entity.Remark = dto.Remark;
                repo.Update(entity);
            }
        }

        await repo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
