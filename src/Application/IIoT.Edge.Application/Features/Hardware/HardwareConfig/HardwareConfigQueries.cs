using AutoMapper;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Mappings;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;
using MediatR;

namespace IIoT.Edge.Application.Features.Hardware.HardwareConfigView;

public record HardwareConfigInitResult(
    List<NetworkDeviceVm> NetworkDevices,
    List<SerialDeviceVm> SerialDevices);

public record IoMappingPageResult(
    List<IoMappingVm> Items,
    int TotalCount);

public record ModuleTemplateInfoResult(
    bool IsAvailable,
    string? ModuleId,
    string Summary);

public record LoadHardwareConfigQuery : IRequest<HardwareConfigInitResult>;

public record LoadIoMappingsQuery(int NetworkDeviceId, int PageIndex, int PageSize)
    : IRequest<IoMappingPageResult>;

public record SaveHardwareConfigCommand(
    List<NetworkDeviceVm> NetworkDevices,
    List<SerialDeviceVm> SerialDevices,
    int SelectedNetworkDeviceId,
    List<IoMappingVm> IoMappings) : IRequest;

public class LoadHardwareConfigHandler(ISender sender, IMapper mapper)
    : IRequestHandler<LoadHardwareConfigQuery, HardwareConfigInitResult>
{
    public async Task<HardwareConfigInitResult> Handle(LoadHardwareConfigQuery request, CancellationToken ct)
    {
        var networkResult = await sender.Send(new GetAllNetworkDevicesQuery(), ct);
        var networks = new List<NetworkDeviceVm>();
        if (networkResult.IsSuccess && networkResult.Value != null)
        {
            foreach (var network in networkResult.Value)
            {
                networks.Add(mapper.Map<NetworkDeviceVm>(network));
            }
        }

        var serialResult = await sender.Send(new GetAllSerialDevicesQuery(), ct);
        var serials = new List<SerialDeviceVm>();
        if (serialResult.IsSuccess && serialResult.Value != null)
        {
            foreach (var serial in serialResult.Value)
            {
                serials.Add(mapper.Map<SerialDeviceVm>(serial));
            }
        }

        return new HardwareConfigInitResult(networks, serials);
    }
}

public class LoadIoMappingsHandler(ISender sender, IMapper mapper)
    : IRequestHandler<LoadIoMappingsQuery, IoMappingPageResult>
{
    public async Task<IoMappingPageResult> Handle(LoadIoMappingsQuery request, CancellationToken ct)
    {
        var result = await sender.Send(
            new GetIoMappingsByDeviceQuery(request.NetworkDeviceId, request.PageIndex, request.PageSize),
            ct);

        if (!result.IsSuccess || result.Value is null)
        {
            return new IoMappingPageResult(new(), 0);
        }

        var items = result.Value.Items
            .Select(item => mapper.Map<IoMappingVm>(item))
            .ToList();

        return new IoMappingPageResult(items, result.Value.TotalCount);
    }
}

public class SaveHardwareConfigHandler(ISender sender, IMapper mapper)
    : IRequestHandler<SaveHardwareConfigCommand>
{
    public async Task Handle(SaveHardwareConfigCommand request, CancellationToken ct)
    {
        var networkDtos = mapper.Map<List<NetworkDeviceDto>>(request.NetworkDevices);
        await sender.Send(new SaveNetworkDevicesCommand(networkDtos), ct);

        var serialDtos = mapper.Map<List<SerialDeviceDto>>(request.SerialDevices);
        await sender.Send(new SaveSerialDevicesCommand(serialDtos), ct);

        if (request.SelectedNetworkDeviceId != 0)
        {
            var ioDtos = request.IoMappings
                .Select(vm => mapper.Map<IoMappingDto>(vm, opts =>
                {
                    opts.Items[HardwareConfigMappingProfile.NetworkDeviceIdContextKey] =
                        request.SelectedNetworkDeviceId;
                }))
                .ToList();

            await sender.Send(new SaveIoMappingsCommand(request.SelectedNetworkDeviceId, ioDtos), ct);
        }
    }
}
