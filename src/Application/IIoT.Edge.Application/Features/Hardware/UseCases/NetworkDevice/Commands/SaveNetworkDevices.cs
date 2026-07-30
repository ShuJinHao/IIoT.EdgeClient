using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;

/// <summary>
/// 单条网络设备的数据传输对象。
/// </summary>
public record NetworkDeviceDto(
    int Id,
    string DeviceName,
    DeviceType DeviceType,
    string? DeviceModel,
    string IpAddress,
    int Port1,
    int? Port2,
    string? SendCmd1,
    string? SendCmd2,
    int ConnectTimeout,
    bool IsEnabled,
    string? Remark,
    string? ProtocolFrame = null,
    string PlcCode = ""
);

/// <summary>
/// 命令：保存网络设备列表，按提交结果进行新增或更新。
/// </summary>
public record SaveNetworkDevicesCommand(
    List<NetworkDeviceDto> Devices
) : ICommand<Result>;

/// <summary>
/// 处理器：保存网络设备配置。
/// </summary>
public class SaveNetworkDevicesHandler(
    IEdgeUnitOfWorkFactory unitOfWorkFactory
) : ICommandHandler<SaveNetworkDevicesCommand, Result>
{
    public async Task<Result> Handle(
        SaveNetworkDevicesCommand request,
        CancellationToken cancellationToken)
    {
        return await SubmittedEntityListSaveHelper.ExecuteInUnitOfWorkAsync<NetworkDeviceEntity>(
            unitOfWorkFactory,
            (repo, ct) => ApplyAsync(repo, request, ct),
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<Result> ApplyAsync(
        IRepository<NetworkDeviceEntity> repo,
        SaveNetworkDevicesCommand request,
        CancellationToken cancellationToken)
        => await ApplyCoreAsync(
            repo,
            request,
            existingIdsToUpdate: null,
            existingIdsToDelete: null,
            createdEntities: null,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<Result> ApplyPlannedAsync(
        IRepository<NetworkDeviceEntity> repo,
        SaveNetworkDevicesCommand request,
        IReadOnlySet<int> existingIdsToUpdate,
        IReadOnlySet<int> existingIdsToDelete,
        ICollection<NetworkDeviceEntity> createdEntities,
        CancellationToken cancellationToken)
        => await ApplyCoreAsync(
            repo,
            request,
            existingIdsToUpdate,
            existingIdsToDelete,
            createdEntities,
            cancellationToken).ConfigureAwait(false);

    private static async Task<Result> ApplyCoreAsync(
        IRepository<NetworkDeviceEntity> repo,
        SaveNetworkDevicesCommand request,
        IReadOnlySet<int>? existingIdsToUpdate,
        IReadOnlySet<int>? existingIdsToDelete,
        ICollection<NetworkDeviceEntity>? createdEntities,
        CancellationToken cancellationToken)
    {
        var existingItems = await repo.GetListAsync(_ => true, cancellationToken).ConfigureAwait(false);
        var submittedIds = request.Devices
            .Select(static dto => dto.Id)
            .Where(static id => id > 0)
            .ToHashSet();
        var usedPlcCodes = existingItems
            .Where(entity => submittedIds.Contains(entity.Id))
            .Select(static entity => entity.PlcCode)
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return await SubmittedEntityListSaveHelper.ReplaceSubmittedAsync(
            repo,
            request.Devices,
            _ => Task.FromResult(existingItems),
            static dto => dto.Id,
            Validate,
            dto => TrackCreatedEntity(
                CreateWithUniquePlcCode(dto, usedPlcCodes),
                createdEntities),
            Apply,
            entity => existingIdsToDelete is null || existingIdsToDelete.Contains(entity.Id),
            (entity, _) => existingIdsToUpdate is null || existingIdsToUpdate.Contains(entity.Id),
            cancellationToken).ConfigureAwait(false);
    }

    private static NetworkDeviceEntity TrackCreatedEntity(
        NetworkDeviceEntity entity,
        ICollection<NetworkDeviceEntity>? createdEntities)
    {
        createdEntities?.Add(entity);
        return entity;
    }

    private static NetworkDeviceEntity Create(NetworkDeviceDto dto)
        => NetworkDeviceEntity.Create(
            dto.DeviceName,
            dto.DeviceType,
            dto.IpAddress,
            dto.Port1);

    private static NetworkDeviceEntity CreateWithUniquePlcCode(
        NetworkDeviceDto dto,
        ISet<string> usedPlcCodes)
    {
        var entity = Create(dto);
        if (usedPlcCodes.Add(entity.PlcCode))
        {
            return entity;
        }

        string fallbackCode;
        do
        {
            fallbackCode = NetworkDeviceEntity.CreateInternalPlcCode();
        }
        while (!usedPlcCodes.Add(fallbackCode));

        return NetworkDeviceEntity.Create(
            dto.DeviceName,
            dto.DeviceType,
            dto.IpAddress,
            dto.Port1,
            fallbackCode);
    }

    private static void Apply(NetworkDeviceEntity entity, NetworkDeviceDto dto)
    {
        entity.Rename(dto.DeviceName);
        entity.ChangeType(dto.DeviceType);
        entity.UpdateDeviceModel(dto.DeviceModel);
        entity.UpdateProtocolFrame(NormalizeProtocolFrame(dto));
        entity.UpdateEndpoint(dto.IpAddress, dto.Port1, dto.Port2, dto.ConnectTimeout);
        entity.UpdateCommands(dto.SendCmd1, dto.SendCmd2);
        entity.SetEnabled(dto.IsEnabled);
        entity.UpdateRemark(dto.Remark);
    }

    private static string? Validate(NetworkDeviceDto dto)
    {
        try
        {
            var entity = Create(dto);
            Apply(entity, dto);
            return ValidateProtocolFrame(dto);
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    private static string? ValidateProtocolFrame(NetworkDeviceDto dto)
    {
        if (!IsMcPlc(dto) || string.IsNullOrWhiteSpace(dto.ProtocolFrame))
        {
            return null;
        }

        return IsSupportedMcFrame(dto.ProtocolFrame)
            ? null
            : "MC PLC 协议帧只支持 E3 或 E4。";
    }

    private static string? NormalizeProtocolFrame(NetworkDeviceDto dto)
        => IsMcPlc(dto) ? Normalize(dto.ProtocolFrame) : null;

    private static bool IsMcPlc(NetworkDeviceDto dto)
        => dto.DeviceType == DeviceType.PLC
           && string.Equals(dto.DeviceModel?.Trim(), PlcType.Mc.ToString(), StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedMcFrame(string value)
        => string.Equals(value.Trim(), nameof(IIoT.Edge.Module.Contracts.Plc.McPlcFrameType.E3), StringComparison.OrdinalIgnoreCase)
           || string.Equals(value.Trim(), nameof(IIoT.Edge.Module.Contracts.Plc.McPlcFrameType.E4), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
}
