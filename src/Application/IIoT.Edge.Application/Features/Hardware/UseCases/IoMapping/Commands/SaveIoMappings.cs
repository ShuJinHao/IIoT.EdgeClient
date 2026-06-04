using IIoT.Edge.Application.Common.Crud;
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
        => await SubmittedEntityListSaveHelper.ReplaceSubmittedAsync(
            repo,
            request.Mappings,
            ct => repo.GetListAsync(x => x.NetworkDeviceId == request.NetworkDeviceId, ct),
            static dto => dto.Id,
            dto => Validate(request.NetworkDeviceId, dto),
            dto => Create(request.NetworkDeviceId, dto),
            (entity, dto) => Apply(entity, request.NetworkDeviceId, dto),
            cancellationToken).ConfigureAwait(false);

    private static IoMappingEntity Create(int networkDeviceId, IoMappingDto dto)
        => IoMappingEntity.Create(
            networkDeviceId,
            dto.SignalKey,
            dto.PlcAddress,
            dto.AddressCount,
            dto.DataType,
            dto.Direction,
            Normalize(dto.Category, "单点读数据"),
            dto.BusinessGroup ?? string.Empty);

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
            var entity = Create(networkDeviceId, dto);
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
