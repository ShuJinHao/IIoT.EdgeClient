using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Result;

namespace IIoT.Edge.Application.Features.Hardware.Queries;

/// <summary>
/// IO 映射分页结果。
/// </summary>
public record IoMappingPagedDto(
    List<DevicePluginIoPointSnapshot> Items,
    int TotalCount
);

/// <summary>
/// 查询：分页获取指定网络设备的 IO 映射。
/// </summary>
public record GetIoMappingsByDeviceQuery(
    int NetworkDeviceId,
    int PageIndex,
    int PageSize
) : IQuery<Result<IoMappingPagedDto>>;
