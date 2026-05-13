using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Application.Features.Hardware.Queries;

/// <summary>
/// 查询：获取全部串口设备配置。
/// </summary>
public record GetAllSerialDevicesQuery() : IQuery<Result<List<SerialDeviceEntity>>>;
