using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

/// <summary>
/// 网络设备校验器。
/// </summary>
internal sealed class NetworkDeviceValidator : IEditorValidator<NetworkDeviceVm>
{
    public Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(
        NetworkDeviceVm model,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model.DeviceName))
            issues.Add(new ValidationIssue("网络设备名称不能为空。", nameof(model.DeviceName)));

        if (model.DeviceType == IIoT.Edge.SharedKernel.Enums.DeviceType.PLC
            && string.IsNullOrWhiteSpace(model.ModuleId))
            issues.Add(new ValidationIssue($"设备\"{model.DeviceName}\"的 ModuleId 不能为空。", nameof(model.ModuleId)));

        if (string.IsNullOrWhiteSpace(model.IpAddress))
            issues.Add(new ValidationIssue($"设备\"{model.DeviceName}\"的 IP 地址不能为空。", nameof(model.IpAddress)));

        if (model.Port1 <= 0)
            issues.Add(new ValidationIssue($"设备\"{model.DeviceName}\"的主端口必须大于 0。", nameof(model.Port1)));

        return Task.FromResult<IReadOnlyCollection<ValidationIssue>>(issues);
    }
}

/// <summary>
/// 串口设备校验器。
/// </summary>
internal sealed class SerialDeviceValidator : IEditorValidator<SerialDeviceVm>
{
    public Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(
        SerialDeviceVm model,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model.DeviceName))
            issues.Add(new ValidationIssue("串口设备名称不能为空。", nameof(model.DeviceName)));

        if (string.IsNullOrWhiteSpace(model.PortName))
            issues.Add(new ValidationIssue($"设备\"{model.DeviceName}\"的串口号不能为空。", nameof(model.PortName)));

        return Task.FromResult<IReadOnlyCollection<ValidationIssue>>(issues);
    }
}

/// <summary>
/// IO 映射校验器。
/// </summary>
internal sealed class IoMappingValidator : IEditorValidator<IoMappingVm>
{
    public Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(
        IoMappingVm model,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model.Label))
            issues.Add(new ValidationIssue("IO 映射标签不能为空。", nameof(model.Label)));

        if (string.IsNullOrWhiteSpace(model.PlcAddress))
            issues.Add(new ValidationIssue($"IO\"{model.Label}\"的 PLC 地址不能为空。", nameof(model.PlcAddress)));

        if (model.AddressCount <= 0)
            issues.Add(new ValidationIssue($"IO\"{model.Label}\"的地址长度必须大于 0。", nameof(model.AddressCount)));

        return Task.FromResult<IReadOnlyCollection<ValidationIssue>>(issues);
    }
}
