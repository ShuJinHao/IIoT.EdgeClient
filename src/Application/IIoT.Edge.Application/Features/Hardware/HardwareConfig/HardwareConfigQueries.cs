using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Enums;
using MediatR;

namespace IIoT.Edge.Application.Features.Hardware.HardwareConfigView;

public record HardwareConfigInitResult(
    List<NetworkDeviceDto> NetworkDevices,
    List<SerialDeviceDto> SerialDevices);

public record IoMappingPageResult(
    List<IoMappingDto> Items,
    int TotalCount);

public record ModuleTemplateInfoResult(
    bool IsAvailable,
    IReadOnlyList<ModuleIoTemplateEntry> DefaultSignals,
    IReadOnlyList<ModuleIoTemplateEntry> CandidateSignals,
    string Message);

public record LoadHardwareConfigQuery : IRequest<HardwareConfigInitResult>;

public record LoadIoMappingsQuery(int NetworkDeviceId)
    : IRequest<IoMappingPageResult>;

public record SaveHardwareConfigCommand(
    List<NetworkDeviceDto> NetworkDevices,
    List<SerialDeviceDto> SerialDevices,
    int SelectedNetworkDeviceId,
    List<IoMappingDto> IoMappings) : IRequest<CrudOperationResult>;

public class LoadHardwareConfigHandler(ISender sender)
    : IRequestHandler<LoadHardwareConfigQuery, HardwareConfigInitResult>
{
    public async Task<HardwareConfigInitResult> Handle(LoadHardwareConfigQuery request, CancellationToken ct)
    {
        var networkResult = await sender.Send(new GetAllNetworkDevicesQuery(), ct);
        var networks = new List<NetworkDeviceDto>();
        if (networkResult.IsSuccess && networkResult.Value != null)
        {
            foreach (var network in networkResult.Value)
            {
                networks.Add(MapNetworkDevice(network));
            }
        }

        var serialResult = await sender.Send(new GetAllSerialDevicesQuery(), ct);
        var serials = new List<SerialDeviceDto>();
        if (serialResult.IsSuccess && serialResult.Value != null)
        {
            foreach (var serial in serialResult.Value)
            {
                serials.Add(MapSerialDevice(serial));
            }
        }

        return new HardwareConfigInitResult(networks, serials);
    }

    private static NetworkDeviceDto MapNetworkDevice(NetworkDeviceEntity entity)
        => new(
            entity.Id,
            entity.DeviceName,
            entity.DeviceType,
            entity.DeviceModel,
            entity.IpAddress,
            entity.Port1,
            entity.Port2,
            entity.SendCmd1,
            entity.SendCmd2,
            entity.ConnectTimeout,
            entity.IsEnabled,
            entity.Remark,
            entity.ProtocolFrame);

    private static SerialDeviceDto MapSerialDevice(SerialDeviceEntity entity)
        => new(
            entity.Id,
            entity.DeviceName,
            entity.DeviceType,
            entity.PortName,
            entity.BaudRate,
            entity.DataBits,
            entity.StopBits,
            entity.Parity,
            entity.SendCmd1,
            entity.SendCmd2,
            entity.IsEnabled,
            entity.Remark);
}

public class LoadIoMappingsHandler(ISender sender)
    : IRequestHandler<LoadIoMappingsQuery, IoMappingPageResult>
{
    public async Task<IoMappingPageResult> Handle(LoadIoMappingsQuery request, CancellationToken ct)
    {
        var result = await sender.Send(
            new GetIoMappingsByDeviceQuery(request.NetworkDeviceId, 0, int.MaxValue),
            ct);

        if (!result.IsSuccess || result.Value is null)
        {
            return new IoMappingPageResult(new(), 0);
        }

        var items = result.Value.Items
            .Select(MapIoMapping)
            .ToList();

        return new IoMappingPageResult(items, items.Count);
    }

    private static IoMappingDto MapIoMapping(IoMappingEntity entity)
        => new(
            entity.Id,
            entity.NetworkDeviceId,
            entity.SignalKey,
            entity.PlcAddress,
            entity.AddressCount,
            entity.DataType,
            entity.Direction,
            entity.Category,
            entity.BusinessGroup,
            entity.SortOrder,
            entity.Remark);
}

public class SaveHardwareConfigHandler(
    ISender sender,
    IClientPermissionService permissionService,
    IPlcConnectionManager plcConnectionManager)
    : IRequestHandler<SaveHardwareConfigCommand, CrudOperationResult>
{
    public async Task<CrudOperationResult> Handle(SaveHardwareConfigCommand request, CancellationToken ct)
    {
        if (!permissionService.CanEditHardware)
        {
            return CrudOperationResult.Failure("当前用户没有硬件配置权限。");
        }

        var existingNetworkDevices = await LoadExistingNetworkDevicesAsync(ct);
        var existingIoMappings = await LoadExistingIoMappingsAsync(request.SelectedNetworkDeviceId, ct);

        var networkResult = await sender.Send(new SaveNetworkDevicesCommand(request.NetworkDevices), ct);
        if (!networkResult.IsSuccess)
        {
            return CrudOperationResult.Failure(networkResult.ErrorMessage ?? "网络设备保存失败。");
        }

        var serialResult = await sender.Send(new SaveSerialDevicesCommand(request.SerialDevices), ct);
        if (!serialResult.IsSuccess)
        {
            return CrudOperationResult.Failure(serialResult.ErrorMessage ?? "串口设备保存失败。");
        }

        var selectedDeviceStillExists = request.SelectedNetworkDeviceId != 0
            && request.NetworkDevices.Any(x => x.Id == request.SelectedNetworkDeviceId);

        if (selectedDeviceStillExists)
        {
            var ioDtos = request.IoMappings
                .Select(dto => dto with { NetworkDeviceId = request.SelectedNetworkDeviceId })
                .ToList();

            var ioResult = await sender.Send(new SaveIoMappingsCommand(request.SelectedNetworkDeviceId, ioDtos), ct);
            if (!ioResult.IsSuccess)
            {
                return CrudOperationResult.Failure(ioResult.ErrorMessage ?? "IO 映射保存失败。");
            }
        }

        var stopFailures = new List<string>();
        var reloadFailures = new List<string>();
        var existingPlcById = existingNetworkDevices
            .Where(x => x.DeviceType == DeviceType.PLC)
            .ToDictionary(x => x.Id);
        var submittedPlcDevices = request.NetworkDevices
            .Where(x => x.DeviceType == DeviceType.PLC)
            .ToList();
        var submittedPlcById = submittedPlcDevices
            .Where(x => x.Id > 0)
            .ToDictionary(x => x.Id);

        foreach (var existingPlc in existingPlcById.Values)
        {
            if (submittedPlcById.ContainsKey(existingPlc.Id))
            {
                continue;
            }

            try
            {
                await plcConnectionManager.StopDeviceAsync(existingPlc.Id, ct);
            }
            catch (Exception ex)
            {
                stopFailures.Add($"{existingPlc.DeviceName}（{ex.Message}）");
            }
        }

        var ioMappingsChanged = request.SelectedNetworkDeviceId > 0
            && existingPlcById.TryGetValue(request.SelectedNetworkDeviceId, out _)
            && submittedPlcById.ContainsKey(request.SelectedNetworkDeviceId)
            && HasIoMappingsChanged(existingIoMappings, request.IoMappings, request.SelectedNetworkDeviceId);

        var reloadTargets = new List<(int? DeviceId, string DeviceName)>();
        var reloadTargetIds = new HashSet<int>();
        foreach (var plcDevice in submittedPlcDevices)
        {
            var deviceName = plcDevice.DeviceName?.Trim();
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                continue;
            }

            if (plcDevice.Id == 0)
            {
                reloadTargets.Add((null, deviceName));
                continue;
            }

            if (!existingPlcById.TryGetValue(plcDevice.Id, out var existingPlc))
            {
                if (reloadTargetIds.Add(plcDevice.Id))
                {
                    reloadTargets.Add((plcDevice.Id, deviceName));
                }

                continue;
            }

            if (HasRuntimeRelevantNetworkChange(existingPlc, plcDevice)
                || (ioMappingsChanged && request.SelectedNetworkDeviceId == plcDevice.Id))
            {
                if (reloadTargetIds.Add(plcDevice.Id))
                {
                    reloadTargets.Add((plcDevice.Id, deviceName));
                }
            }
        }

        foreach (var target in reloadTargets)
        {
            try
            {
                await plcConnectionManager.ReloadAsync(target.DeviceName, ct);
            }
            catch (Exception ex)
            {
                reloadFailures.Add($"{target.DeviceName}（{ex.Message}）");
            }
        }

        var runtimeIssues = new List<string>();
        if (stopFailures.Count > 0)
        {
            runtimeIssues.Add($"以下 PLC 已删除停机失败：{string.Join("；", stopFailures)}");
        }

        if (reloadFailures.Count > 0)
        {
            runtimeIssues.Add($"以下 PLC 重载失败：{string.Join("；", reloadFailures)}");
        }

        return runtimeIssues.Count == 0
            ? CrudOperationResult.Success("硬件配置已保存。")
            : CrudOperationResult.Failure($"配置已保存，但 {string.Join("；", runtimeIssues)}");
    }

    private async Task<List<NetworkDeviceEntity>> LoadExistingNetworkDevicesAsync(CancellationToken ct)
    {
        var result = await sender.Send(new GetAllNetworkDevicesQuery(), ct);
        if (!result.IsSuccess || result.Value is null)
        {
            return [];
        }

        return result.Value;
    }

    private async Task<List<IoMappingEntity>> LoadExistingIoMappingsAsync(int networkDeviceId, CancellationToken ct)
    {
        if (networkDeviceId <= 0)
        {
            return [];
        }

        var result = await sender.Send(new GetIoMappingsByDeviceQuery(networkDeviceId, 0, int.MaxValue), ct);
        if (!result.IsSuccess || result.Value is null)
        {
            return [];
        }

        return result.Value.Items;
    }

    private static bool HasRuntimeRelevantNetworkChange(NetworkDeviceEntity existing, NetworkDeviceDto incoming)
    {
        return !string.Equals(existing.DeviceName?.Trim(), incoming.DeviceName?.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.DeviceModel?.Trim(), incoming.DeviceModel?.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.ProtocolFrame?.Trim(), incoming.ProtocolFrame?.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.IpAddress?.Trim(), incoming.IpAddress?.Trim(), StringComparison.OrdinalIgnoreCase)
            || existing.Port1 != incoming.Port1
            || existing.Port2 != incoming.Port2
            || !string.Equals(existing.SendCmd1?.Trim(), incoming.SendCmd1?.Trim(), StringComparison.Ordinal)
            || !string.Equals(existing.SendCmd2?.Trim(), incoming.SendCmd2?.Trim(), StringComparison.Ordinal)
            || existing.ConnectTimeout != incoming.ConnectTimeout
            || existing.IsEnabled != incoming.IsEnabled;
    }

    private static bool HasIoMappingsChanged(
        IReadOnlyCollection<IoMappingEntity> existingMappings,
        IReadOnlyCollection<IoMappingDto> incomingMappings,
        int networkDeviceId)
    {
        var existing = existingMappings
            .Where(x => x.NetworkDeviceId == networkDeviceId)
            .Select(x => new IoMappingSnapshot(
                Normalize(x.SignalKey),
                Normalize(x.PlcAddress),
                x.AddressCount,
                Normalize(x.DataType),
                Normalize(x.Direction),
                x.SortOrder,
                Normalize(x.Category),
                NormalizeNullable(x.BusinessGroup),
                NormalizeNullable(x.Remark)))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.SignalKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PlcAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var incoming = incomingMappings
            .Select(x => new IoMappingSnapshot(
                Normalize(x.SignalKey),
                Normalize(x.PlcAddress),
                x.AddressCount,
                Normalize(x.DataType),
                Normalize(x.Direction),
                x.SortOrder,
                Normalize(x.Category),
                NormalizeNullable(x.BusinessGroup),
                NormalizeNullable(x.Remark)))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.SignalKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PlcAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return !existing.SequenceEqual(incoming);
    }

    private static string Normalize(string value) => value?.Trim() ?? string.Empty;

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private readonly record struct IoMappingSnapshot(
        string SignalKey,
        string PlcAddress,
        int AddressCount,
        string DataType,
        string Direction,
        int SortOrder,
        string Category,
        string? BusinessGroup,
        string? Remark);
}
