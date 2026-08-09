using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Result;

namespace IIoT.Edge.Application.Features.Hardware.Queries;

/// <summary>
/// 查询：获取全部网络设备配置。
/// </summary>
public record GetAllNetworkDevicesQuery() : IQuery<Result<List<DevicePluginPlcSnapshot>>>;
