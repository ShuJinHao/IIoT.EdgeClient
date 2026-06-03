using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;
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
        NetworkDeviceDto? selectedNetworkDevice,
        CancellationToken cancellationToken = default);

    Task<CrudOperationResult> ApplyModuleTemplateAsync(
        NetworkDeviceDto? selectedNetworkDevice,
        CancellationToken cancellationToken = default);

    Task<CrudOperationResult> SaveAsync(
        IReadOnlyCollection<NetworkDeviceDto> networkDevices,
        IReadOnlyCollection<SerialDeviceDto> serialDevices,
        int selectedNetworkDeviceId,
        IReadOnlyCollection<IoMappingDto> ioMappings,
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
    private readonly IModuleHardwareProfileProvider[] _hardwareProfiles = hardwareProfiles
        .OrderBy(static x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public Task<HardwareConfigInitResult> LoadAsync(CancellationToken cancellationToken = default)
        => sender.Send(new LoadHardwareConfigQuery(), cancellationToken);

    public Task<IoMappingPageResult> LoadIoMappingsAsync(
        int networkDeviceId,
        CancellationToken cancellationToken = default)
        => sender.Send(
            new LoadIoMappingsQuery(networkDeviceId),
            cancellationToken);

    public Task<ModuleTemplateInfoResult> GetModuleTemplateInfoAsync(
        NetworkDeviceDto? selectedNetworkDevice,
        CancellationToken cancellationToken = default)
    {
        if (selectedNetworkDevice is null)
        {
            return Task.FromResult(new ModuleTemplateInfoResult(
                false,
                [],
                [],
                "请选择一个 PLC 设备。"));
        }

        if (selectedNetworkDevice.DeviceType != DeviceType.PLC)
        {
            return Task.FromResult(new ModuleTemplateInfoResult(
                false,
                [],
                [],
                "插件标准点位只支持 PLC 设备。"));
        }

        var provider = ResolveHardwareProfile(out var profileError);
        if (provider is null)
        {
            return Task.FromResult(new ModuleTemplateInfoResult(
                false,
                [],
                [],
                profileError ?? "当前插件库没有可用的标准 IO 点位。"));
        }

        var defaultSignals = provider.GetDefaultIoTemplate()
            .OrderBy(static x => x.SortOrder)
            .ThenBy(static x => x.SignalKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var candidateSignals = provider.GetIoMappingCandidates()
            .OrderBy(static x => x.SortOrder)
            .ThenBy(static x => x.SignalKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidateSignals.Length == 0)
        {
            return Task.FromResult(new ModuleTemplateInfoResult(
                false,
                [],
                [],
                "当前插件没有标准 IO 点位。"));
        }

        return Task.FromResult(new ModuleTemplateInfoResult(
            true,
            defaultSignals,
            candidateSignals,
            "重置当前 PLC 的 IO 映射为插件标准点位，会清理旧手工错误点位。"));
    }

    public async Task<CrudOperationResult> ApplyModuleTemplateAsync(
        NetworkDeviceDto? selectedNetworkDevice,
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
            return CrudOperationResult.Failure("请先保存设备，再重置插件标准点位。");
        }

        var provider = ResolveHardwareProfile(out var profileError);
        if (provider is null)
        {
            return CrudOperationResult.Failure(profileError ?? "当前插件库没有可用的标准 IO 点位。");
        }

        var resetMappings = provider.GetIoMappingCandidates()
            .OrderBy(x => x.SortOrder)
            .Select(template => new IoMappingDto(
                0,
                selectedNetworkDevice.Id,
                template.SignalKey,
                template.PlcAddress,
                template.AddressCount,
                template.DataType,
                template.Direction,
                template.Category,
                template.BusinessGroup,
                template.SortOrder,
                template.Remark))
            .ToList();

        await sender.Send(
            new SaveIoMappingsCommand(selectedNetworkDevice.Id, resetMappings),
            cancellationToken);

        return CrudOperationResult.Success($"已重置 {resetMappings.Count} 条插件标准点位。");
    }

    public Task<CrudOperationResult> SaveAsync(
        IReadOnlyCollection<NetworkDeviceDto> networkDevices,
        IReadOnlyCollection<SerialDeviceDto> serialDevices,
        int selectedNetworkDeviceId,
        IReadOnlyCollection<IoMappingDto> ioMappings,
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

    private IModuleHardwareProfileProvider? ResolveHardwareProfile(out string? errorMessage)
    {
        if (_hardwareProfiles.Length == 0)
        {
            errorMessage = "当前插件库没有注册标准 IO 点位模板。";
            return null;
        }

        if (_hardwareProfiles.Length > 1)
        {
            errorMessage = "当前数据库应只对应一个插件模板；请按插件独立库运行，不能在设备表里用模块 ID 区分工序。";
            return null;
        }

        errorMessage = null;
        return _hardwareProfiles[0];
    }
}
