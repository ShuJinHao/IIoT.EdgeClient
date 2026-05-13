using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Queries;


/// <summary>
/// 处理器：获取全部串口设备配置。
/// </summary>
public class GetAllSerialDevicesHandler(
    IReadRepository<SerialDeviceEntity> repo
) : IQueryHandler<GetAllSerialDevicesQuery, Result<List<SerialDeviceEntity>>>
{
    public async Task<Result<List<SerialDeviceEntity>>> Handle(
        GetAllSerialDevicesQuery request,
        CancellationToken cancellationToken)
    {
        var list = await repo.GetListAsync(_ => true, cancellationToken);
        return Result.Success(list);
    }
}
