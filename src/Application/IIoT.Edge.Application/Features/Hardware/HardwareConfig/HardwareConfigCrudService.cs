using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.SharedKernel.Enums;
using MediatR;

namespace IIoT.Edge.Application.Features.Hardware.HardwareConfigView;

/// <summary>
/// 硬件配置页增删改查服务契约。
/// </summary>
public interface IHardwareConfigCrudService
{
    Task<HardwareConfigInitResult> LoadAsync(CancellationToken cancellationToken = default);

    Task<IoMappingPageResult> LoadIoMappingsAsync(
        int networkDeviceId,
        CancellationToken cancellationToken = default);

    Task<ModuleTemplateInfoResult> GetModuleTemplateInfoAsync(
        NetworkDeviceVm? selectedNetworkDevice,
        CancellationToken cancellationToken = default);

    Task<CrudOperationResult> ApplyModuleTemplateAsync(
        NetworkDeviceVm? selectedNetworkDevice,
        CancellationToken cancellationToken = default);

    Task<CrudOperationResult> SaveAsync(
        IReadOnlyCollection<NetworkDeviceVm> networkDevices,
        IReadOnlyCollection<SerialDeviceVm> serialDevices,
        int selectedNetworkDeviceId,
        IReadOnlyCollection<IoMappingVm> ioMappings,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 硬件配置页服务只负责转发界面操作，不直接连接 PLC。
/// </summary>
public sealed class HardwareConfigCrudService(
    ISender sender,
    IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles,
    IClientPermissionService permissionService) : IHardwareConfigCrudService
{
    private readonly Dictionary<string, IModuleHardwareProfileProvider> _hardwareProfiles = hardwareProfiles
        .ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);

    public Task<HardwareConfigInitResult> LoadAsync(CancellationToken cancellationToken = default)
        => sender.Send(new LoadHardwareConfigQuery(), cancellationToken);

    public Task<IoMappingPageResult> LoadIoMappingsAsync(
        int networkDeviceId,
        CancellationToken cancellationToken = default)
        => sender.Send(
            new LoadIoMappingsQuery(networkDeviceId),
            cancellationToken);

    public Task<ModuleTemplateInfoResult> GetModuleTemplateInfoAsync(
        NetworkDeviceVm? selectedNetworkDevice,
        CancellationToken cancellationToken = default)
    {
        if (selectedNetworkDevice is null)
        {
            return Task.FromResult(new ModuleTemplateInfoResult(
                false,
                null,
                [],
                "请选择一个 PLC 设备。"));
        }

        if (selectedNetworkDevice.DeviceType != DeviceType.PLC)
        {
            return Task.FromResult(new ModuleTemplateInfoResult(
                false,
                selectedNetworkDevice.ModuleId,
                [],
                "插件标准点位只支持 PLC 设备。"));
        }

        if (string.IsNullOrWhiteSpace(selectedNetworkDevice.ModuleId)
            || !_hardwareProfiles.TryGetValue(selectedNetworkDevice.ModuleId, out var provider))
        {
            return Task.FromResult(new ModuleTemplateInfoResult(
                false,
                selectedNetworkDevice.ModuleId,
                [],
                "当前 PLC 未绑定可用的插件标准点位。"));
        }

        var defaultSignals = provider.GetDefaultIoTemplate()
            .OrderBy(static x => x.SortOrder)
            .ThenBy(static x => x.SignalKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (defaultSignals.Length == 0)
        {
            return Task.FromResult(new ModuleTemplateInfoResult(
                false,
                provider.ModuleId,
                [],
                "当前模块没有插件标准 IO 点位。"));
        }

        return Task.FromResult(new ModuleTemplateInfoResult(
            true,
            provider.ModuleId,
            defaultSignals,
            "只导入当前 PLC 缺失的插件标准点位，不覆盖已维护地址。"));
    }

    public async Task<CrudOperationResult> ApplyModuleTemplateAsync(
        NetworkDeviceVm? selectedNetworkDevice,
        CancellationToken cancellationToken = default)
    {
        if (!permissionService.CanEditHardware)
        {
            return CrudOperationResult.Failure("当前用户没有硬件配置权限。");
        }

        if (selectedNetworkDevice is null)
        {
            return CrudOperationResult.Failure("请先选择一个 PLC 设备。");
        }

        if (selectedNetworkDevice.DeviceType != DeviceType.PLC)
        {
            return CrudOperationResult.Failure("插件标准点位只支持 PLC 设备。");
        }

        if (selectedNetworkDevice.Id <= 0)
        {
            return CrudOperationResult.Failure("请先保存设备，再导入插件标准点位。");
        }

        if (string.IsNullOrWhiteSpace(selectedNetworkDevice.ModuleId)
            || !_hardwareProfiles.TryGetValue(selectedNetworkDevice.ModuleId, out var provider))
        {
            return CrudOperationResult.Failure("当前 PLC 未绑定可用的插件标准点位。");
        }

        var existingMappings = await sender.Send(
            new GetIoMappingsByDeviceQuery(selectedNetworkDevice.Id, 0, int.MaxValue),
            cancellationToken);

        if (!existingMappings.IsSuccess || existingMappings.Value is null)
        {
            return CrudOperationResult.Failure("加载当前 IO 映射失败，无法导入插件标准点位。");
        }

        var allMappings = existingMappings.Value.Items
            .Select(static x => new IoMappingDto(
                x.Id,
                x.NetworkDeviceId,
                x.SignalKey,
                x.PlcAddress,
                x.AddressCount,
                x.DataType,
                x.Direction,
                x.Category,
                x.BusinessGroup,
                x.SignalName,
                x.SortOrder,
                x.Remark))
            .ToList();

        var existingSignalKeys = new HashSet<string>(
            allMappings.Select(x => x.SignalKey),
            StringComparer.OrdinalIgnoreCase);

        var addedCount = 0;
        foreach (var template in provider.GetDefaultIoTemplate().OrderBy(x => x.SortOrder))
        {
            if (existingSignalKeys.Contains(template.SignalKey))
            {
                continue;
            }

            allMappings.Add(new IoMappingDto(
                0,
                selectedNetworkDevice.Id,
                template.SignalKey,
                template.PlcAddress,
                template.AddressCount,
                template.DataType,
                template.Direction,
                template.Category,
                template.BusinessGroup,
                template.SignalName,
                template.SortOrder,
                null));
            existingSignalKeys.Add(template.SignalKey);
            addedCount++;
        }

        if (addedCount == 0)
        {
            return CrudOperationResult.Success("插件标准点位已全部存在，无需补充映射。");
        }

        await sender.Send(
            new SaveIoMappingsCommand(selectedNetworkDevice.Id, allMappings),
            cancellationToken);

        return CrudOperationResult.Success($"已导入 {addedCount} 条插件标准点位。");
    }

    public Task<CrudOperationResult> SaveAsync(
        IReadOnlyCollection<NetworkDeviceVm> networkDevices,
        IReadOnlyCollection<SerialDeviceVm> serialDevices,
        int selectedNetworkDeviceId,
        IReadOnlyCollection<IoMappingVm> ioMappings,
        CancellationToken cancellationToken = default)
    {
        if (!permissionService.CanEditHardware)
        {
            return Task.FromResult(CrudOperationResult.Failure("当前用户没有硬件配置权限。"));
        }

        return sender.Send(
            new SaveHardwareConfigCommand(
                networkDevices.ToList(),
                serialDevices.ToList(),
                selectedNetworkDeviceId,
                ioMappings.ToList()),
            cancellationToken);
    }
}
