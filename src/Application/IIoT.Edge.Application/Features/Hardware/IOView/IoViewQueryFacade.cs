using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Result;
using MediatR;

namespace IIoT.Edge.Application.Features.Hardware.IOView;

/// <summary>
/// IO 交互页查询门面，隔离 Presentation ViewModel 对 MediatR 请求总线的直接依赖。
/// </summary>
public interface IIoViewQueryFacade
{
    Task<Result<List<NetworkDeviceEntity>>> GetNetworkDevicesAsync(CancellationToken cancellationToken = default);

    Task<Result<IoMappingPagedDto>> GetIoMappingsAsync(
        int networkDeviceId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);
}

public sealed class IoViewQueryFacade(ISender sender) : IIoViewQueryFacade
{
    public Task<Result<List<NetworkDeviceEntity>>> GetNetworkDevicesAsync(CancellationToken cancellationToken = default)
        => sender.Send(new GetAllNetworkDevicesQuery(), cancellationToken);

    public Task<Result<IoMappingPagedDto>> GetIoMappingsAsync(
        int networkDeviceId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
        => sender.Send(new GetIoMappingsByDeviceQuery(networkDeviceId, pageIndex, pageSize), cancellationToken);
}
