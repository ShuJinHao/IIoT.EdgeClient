using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

/// <summary>
/// 网络设备校验器。
/// </summary>
internal sealed class NetworkDeviceValidator : IEditorValidator<NetworkDeviceVm>
{
    private readonly Func<string, string, string> _getText;
    private readonly Func<string, string, object[], string> _formatText;

    public NetworkDeviceValidator(
        Func<string, string, string> getText,
        Func<string, string, object[], string> formatText)
    {
        _getText = getText;
        _formatText = formatText;
    }

    public Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(
        NetworkDeviceVm model,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model.DeviceName))
            issues.Add(new ValidationIssue(
                _getText("Navigation_Hardware_Validation_NetworkDeviceNameRequired", "网络设备名称不能为空。"),
                nameof(model.DeviceName)));

        if (model.DeviceType == IIoT.Edge.SharedKernel.Enums.DeviceType.PLC
            && string.IsNullOrWhiteSpace(model.ModuleId))
            issues.Add(new ValidationIssue(
                _formatText(
                    "Navigation_Hardware_Validation_NetworkDeviceModuleRequiredFormat",
                    "设备“{0}”的 ModuleId 不能为空。",
                    [model.DeviceName]),
                nameof(model.ModuleId)));

        if (string.IsNullOrWhiteSpace(model.IpAddress))
            issues.Add(new ValidationIssue(
                _formatText(
                    "Navigation_Hardware_Validation_NetworkDeviceIpRequiredFormat",
                    "设备“{0}”的 IP 地址不能为空。",
                    [model.DeviceName]),
                nameof(model.IpAddress)));

        if (model.Port1 <= 0)
            issues.Add(new ValidationIssue(
                _formatText(
                    "Navigation_Hardware_Validation_NetworkDeviceMainPortPositiveFormat",
                    "设备“{0}”的主端口必须大于 0。",
                    [model.DeviceName]),
                nameof(model.Port1)));

        return Task.FromResult<IReadOnlyCollection<ValidationIssue>>(issues);
    }
}

/// <summary>
/// 串口设备校验器。
/// </summary>
internal sealed class SerialDeviceValidator : IEditorValidator<SerialDeviceVm>
{
    private readonly Func<string, string, string> _getText;
    private readonly Func<string, string, object[], string> _formatText;

    public SerialDeviceValidator(
        Func<string, string, string> getText,
        Func<string, string, object[], string> formatText)
    {
        _getText = getText;
        _formatText = formatText;
    }

    public Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(
        SerialDeviceVm model,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model.DeviceName))
            issues.Add(new ValidationIssue(
                _getText("Navigation_Hardware_Validation_SerialDeviceNameRequired", "串口设备名称不能为空。"),
                nameof(model.DeviceName)));

        if (string.IsNullOrWhiteSpace(model.PortName))
            issues.Add(new ValidationIssue(
                _formatText(
                    "Navigation_Hardware_Validation_SerialPortNameRequiredFormat",
                    "设备“{0}”的串口号不能为空。",
                    [model.DeviceName]),
                nameof(model.PortName)));

        return Task.FromResult<IReadOnlyCollection<ValidationIssue>>(issues);
    }
}

/// <summary>
/// IO 映射校验器。
/// </summary>
internal sealed class IoMappingValidator : IEditorValidator<IoMappingVm>
{
    private readonly Func<string, string, string> _getText;
    private readonly Func<string, string, object[], string> _formatText;

    public IoMappingValidator(
        Func<string, string, string> getText,
        Func<string, string, object[], string> formatText)
    {
        _getText = getText;
        _formatText = formatText;
    }

    public Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(
        IoMappingVm model,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model.Label))
            issues.Add(new ValidationIssue(
                _getText("Navigation_Hardware_Validation_IoLabelRequired", "IO 映射标签不能为空。"),
                nameof(model.Label)));

        if (string.IsNullOrWhiteSpace(model.PlcAddress))
            issues.Add(new ValidationIssue(
                _formatText(
                    "Navigation_Hardware_Validation_IoAddressRequiredFormat",
                    "IO“{0}”的 PLC 地址不能为空。",
                    [model.Label]),
                nameof(model.PlcAddress)));

        if (model.AddressCount <= 0)
            issues.Add(new ValidationIssue(
                _formatText(
                    "Navigation_Hardware_Validation_IoAddressCountPositiveFormat",
                    "IO“{0}”的地址长度必须大于 0。",
                    [model.Label]),
                nameof(model.AddressCount)));

        if (string.IsNullOrWhiteSpace(model.Category))
            issues.Add(new ValidationIssue(
                _formatText(
                    "Navigation_Hardware_Validation_IoCategoryRequiredFormat",
                    "IO“{0}”的分类不能为空。",
                    [model.Label]),
                nameof(model.Category)));

        return Task.FromResult<IReadOnlyCollection<ValidationIssue>>(issues);
    }
}
