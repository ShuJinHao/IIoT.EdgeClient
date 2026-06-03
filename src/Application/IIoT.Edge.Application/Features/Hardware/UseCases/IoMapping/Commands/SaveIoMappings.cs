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
    string SignalKey,
    string PlcAddress,
    int AddressCount,
    string DataType,
    string Direction,
    string Category,
    string BusinessGroup,
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

        foreach (var dto in request.Mappings)
        {
            var validationError = Validate(request.NetworkDeviceId, dto);
            if (validationError is not null)
            {
                return Result.Failure(validationError);
            }
        }

        foreach (var entity in existingMappings.Where(x => !submittedIds.Contains(x.Id)))
        {
            repo.Delete(entity);
        }

        foreach (var dto in request.Mappings)
        {
            try
            {
                if (dto.Id == 0)
                {
                    var entity = IoMappingEntity.Create(
                        request.NetworkDeviceId,
                        dto.SignalKey,
                        dto.PlcAddress,
                        dto.AddressCount,
                        dto.DataType,
                        dto.Direction,
                        Normalize(dto.Category, "单点读数据"),
                        dto.BusinessGroup ?? string.Empty);
                    Apply(entity, request.NetworkDeviceId, dto);
                    repo.Add(entity);
                }
                else if (existingById.TryGetValue(dto.Id, out var entity))
                {
                    Apply(entity, request.NetworkDeviceId, dto);
                    repo.Update(entity);
                }
            }
            catch (ArgumentException ex)
            {
                return Result.Failure(ex.Message);
            }
        }

        await repo.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static void Apply(IoMappingEntity entity, int networkDeviceId, IoMappingDto dto)
    {
        entity.BindNetworkDevice(networkDeviceId);
        entity.UpdateAddress(dto.PlcAddress, dto.AddressCount);
        entity.UpdateMetadata(
            dto.SignalKey,
            dto.DataType,
            dto.Direction,
            Normalize(dto.Category, "单点读数据"),
            dto.BusinessGroup,
            dto.Remark);
        entity.UpdateSortOrder(dto.SortOrder);
    }

    private static string? Validate(int networkDeviceId, IoMappingDto dto)
    {
        try
        {
            var entity = IoMappingEntity.Create(
                networkDeviceId,
                dto.SignalKey,
                dto.PlcAddress,
                dto.AddressCount,
                dto.DataType,
                dto.Direction,
                Normalize(dto.Category, "单点读数据"),
                dto.BusinessGroup);
            Apply(entity, networkDeviceId, dto);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private static string Normalize(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
