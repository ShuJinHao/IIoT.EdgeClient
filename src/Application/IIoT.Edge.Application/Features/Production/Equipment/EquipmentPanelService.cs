using MediatR;

namespace IIoT.Edge.Application.Features.Production.Equipment;

/// <summary>
/// 右侧设备信息面板服务契约。
/// </summary>
public interface IEquipmentPanelService
{
    Task<List<HardwareSnapshot>> GetHardwareStatusAsync(CancellationToken cancellationToken = default);

    Task<RecipeSnapshot?> GetRecipeSnapshotAsync(CancellationToken cancellationToken = default);

    Task<CapacitySnapshot> GetCapacitySnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 右侧设备信息面板服务。
/// 负责转发硬件状态、配方快照和产能快照查询。
/// </summary>
public sealed class EquipmentPanelService(ISender sender) : IEquipmentPanelService
{
    public Task<List<HardwareSnapshot>> GetHardwareStatusAsync(CancellationToken cancellationToken = default)
        => sender.Send(new GetHardwareStatusQuery(), cancellationToken);

    public Task<RecipeSnapshot?> GetRecipeSnapshotAsync(CancellationToken cancellationToken = default)
        => sender.Send(new GetRecipeSnapshotQuery(), cancellationToken);

    public Task<CapacitySnapshot> GetCapacitySnapshotAsync(CancellationToken cancellationToken = default)
        => sender.Send(new GetCapacitySnapshotQuery(), cancellationToken);
}
