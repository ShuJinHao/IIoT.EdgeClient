using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.SharedKernel.Enums;
using MediatR;

namespace IIoT.Edge.Application.Features.Hardware.HardwareConfigView;

/// <summary>
/// 硬件配置页面增删改查服务契约。
/// </summary>
public interface IHardwareConfigCrudService
{
    Task<HardwareConfigInitResult> LoadAsync(CancellationToken cancellationToken = default);

    Task<IoMappingPageResult> LoadIoMappingsAsync(
        int networkDeviceId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ModuleTemplateInfoResult> GetModuleTemplateInfoAsync(
        NetworkDeviceVm? selectedNetworkDevice,
        CancellationToken cancellationToken = default);

    Task<CrudOperationResult> ApplyModuleTemplateAsync(
        NetworkDeviceVm? selectedNetworkDevice,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        IReadOnlyCollection<NetworkDeviceVm> networkDevices,
        IReadOnlyCollection<SerialDeviceVm> serialDevices,
        int selectedNetworkDeviceId,
        IReadOnlyCollection<IoMappingVm> ioMappings,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 硬件配置页面增删改查服务。
/// 负责将界面操作转发到硬件配置查询与保存命令。
/// </summary>
public sealed class HardwareConfigCrudService(
    ISender sender,
    IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles) : IHardwareConfigCrudService
{
    private readonly Dictionary<string, IModuleHardwareProfileProvider> _hardwareProfiles = hardwareProfiles
        .ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);

    public Task<HardwareConfigInitResult> LoadAsync(CancellationToken cancellationToken = default)
        => sender.Send(new LoadHardwareConfigQuery(), cancellationToken);

    public Task<IoMappingPageResult> LoadIoMappingsAsync(
        int networkDeviceId,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken = default)
        => sender.Send(
            new LoadIoMappingsQuery(networkDeviceId, pageIndex, pageSize),
            cancellationToken);

    public Task<ModuleTemplateInfoResult> GetModuleTemplateInfoAsync(
        NetworkDeviceVm? selectedNetworkDevice,
        CancellationToken cancellationToken = default)
    {
        if (selectedNetworkDevice is null
            || selectedNetworkDevice.DeviceType != DeviceType.PLC
            || string.IsNullOrWhiteSpace(selectedNetworkDevice.ModuleId)
            || !_hardwareProfiles.TryGetValue(selectedNetworkDevice.ModuleId, out var provider))
        {
            return Task.FromResult(new ModuleTemplateInfoResult(false, selectedNetworkDevice?.ModuleId, string.Empty));
        }

        return Task.FromResult(new ModuleTemplateInfoResult(
            true,
            provider.ModuleId,
            provider.GetProtocolSummary()));
    }

    public async Task<CrudOperationResult> ApplyModuleTemplateAsync(
        NetworkDeviceVm? selectedNetworkDevice,
        CancellationToken cancellationToken = default)
    {
        if (selectedNetworkDevice is null)
        {
            return CrudOperationResult.Failure("请先选择一个 PLC 设备。");
        }

        if (selectedNetworkDevice.DeviceType != DeviceType.PLC)
        {
            return CrudOperationResult.Failure("模块模板仅支持 PLC 设备。");
        }

        if (selectedNetworkDevice.Id <= 0)
        {
            return CrudOperationResult.Failure("请先保存设备，再应用模块模板。");
        }

        if (string.IsNullOrWhiteSpace(selectedNetworkDevice.ModuleId)
            || !_hardwareProfiles.TryGetValue(selectedNetworkDevice.ModuleId, out var provider))
        {
            return CrudOperationResult.Failure("当前设备未配置可用的模块模板。");
        }

        var existingMappings = await sender.Send(
            new GetIoMappingsByDeviceQuery(selectedNetworkDevice.Id, 0, int.MaxValue),
            cancellationToken);

        if (!existingMappings.IsSuccess || existingMappings.Value is null)
        {
            return CrudOperationResult.Failure("加载当前 IO 映射失败，无法应用模块模板。");
        }

        var allMappings = existingMappings.Value.Items
            .Select(static x => new IoMappingDto(
                x.Id,
                x.NetworkDeviceId,
                x.Label,
                x.PlcAddress,
                x.AddressCount,
                x.DataType,
                x.Direction,
                x.SortOrder))
            .ToList();

        var existingLabels = new HashSet<string>(
            allMappings.Select(x => x.Label),
            StringComparer.OrdinalIgnoreCase);

        var addedCount = 0;
        foreach (var template in provider.GetDefaultIoTemplate().OrderBy(x => x.SortOrder))
        {
            if (existingLabels.Contains(template.Label))
            {
                continue;
            }

            allMappings.Add(new IoMappingDto(
                0,
                selectedNetworkDevice.Id,
                template.Label,
                template.PlcAddress,
                template.AddressCount,
                template.DataType,
                template.Direction,
                template.SortOrder));
            existingLabels.Add(template.Label);
            addedCount++;
        }

        if (addedCount == 0)
        {
            return CrudOperationResult.Success("模块模板已存在，无需补充映射。");
        }

        await sender.Send(
            new SaveIoMappingsCommand(selectedNetworkDevice.Id, allMappings),
            cancellationToken);

        return CrudOperationResult.Success($"已补齐 {addedCount} 条模块默认映射。");
    }

    public Task SaveAsync(
        IReadOnlyCollection<NetworkDeviceVm> networkDevices,
        IReadOnlyCollection<SerialDeviceVm> serialDevices,
        int selectedNetworkDeviceId,
        IReadOnlyCollection<IoMappingVm> ioMappings,
        CancellationToken cancellationToken = default)
        => sender.Send(
            new SaveHardwareConfigCommand(
                networkDevices.ToList(),
                serialDevices.ToList(),
                selectedNetworkDeviceId,
                ioMappings.ToList()),
            cancellationToken);
}
