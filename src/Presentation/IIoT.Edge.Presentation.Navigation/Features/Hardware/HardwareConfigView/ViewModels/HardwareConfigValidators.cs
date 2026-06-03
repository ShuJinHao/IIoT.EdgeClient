using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

/// <summary>
/// 网络设备校验器。
/// </summary>
internal sealed class NetworkDeviceValidator : IEditorValidator<NetworkDeviceVm>
{
    private readonly IAppLanguageService _languageService;

    public NetworkDeviceValidator(IAppLanguageService languageService)
    {
        _languageService = languageService;
    }

    public Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(
        NetworkDeviceVm model,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model.DeviceName))
            issues.Add(new ValidationIssue(
                _languageService.GetString("Navigation_Hardware_Validation_NetworkDeviceNameRequired", "网络设备名称不能为空。"),
                nameof(model.DeviceName)));

        if (string.IsNullOrWhiteSpace(model.IpAddress))
            issues.Add(new ValidationIssue(
                _languageService.Format(
                    "Navigation_Hardware_Validation_NetworkDeviceIpRequiredFormat",
                    "设备“{0}”的 IP 地址不能为空。",
                    model.DeviceName),
                nameof(model.IpAddress)));

        if (model.Port1 <= 0)
            issues.Add(new ValidationIssue(
                _languageService.Format(
                    "Navigation_Hardware_Validation_NetworkDeviceMainPortPositiveFormat",
                    "设备“{0}”的主端口必须大于 0。",
                    model.DeviceName),
                nameof(model.Port1)));

        return Task.FromResult<IReadOnlyCollection<ValidationIssue>>(issues);
    }
}

/// <summary>
/// 串口设备校验器。
/// </summary>
internal sealed class SerialDeviceValidator : IEditorValidator<SerialDeviceVm>
{
    private readonly IAppLanguageService _languageService;

    public SerialDeviceValidator(IAppLanguageService languageService)
    {
        _languageService = languageService;
    }

    public Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(
        SerialDeviceVm model,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model.DeviceName))
            issues.Add(new ValidationIssue(
                _languageService.GetString("Navigation_Hardware_Validation_SerialDeviceNameRequired", "串口设备名称不能为空。"),
                nameof(model.DeviceName)));

        if (string.IsNullOrWhiteSpace(model.PortName))
            issues.Add(new ValidationIssue(
                _languageService.Format(
                    "Navigation_Hardware_Validation_SerialPortNameRequiredFormat",
                    "设备“{0}”的串口号不能为空。",
                    model.DeviceName),
                nameof(model.PortName)));

        return Task.FromResult<IReadOnlyCollection<ValidationIssue>>(issues);
    }
}

/// <summary>
/// IO 映射校验器。
/// </summary>
internal sealed class IoMappingValidator : IEditorValidator<IoMappingVm>
{
    private readonly IAppLanguageService _languageService;

    public IoMappingValidator(IAppLanguageService languageService)
    {
        _languageService = languageService;
    }

    public Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync(
        IoMappingVm model,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(model.SignalKey))
            issues.Add(new ValidationIssue(
                _languageService.GetString("Navigation_Hardware_Validation_IoSignalKeyRequired", "IO 内部信号键不能为空。"),
                nameof(model.SignalKey)));

        if (string.IsNullOrWhiteSpace(model.PlcAddress))
            issues.Add(new ValidationIssue(
                _languageService.Format(
                    "Navigation_Hardware_Validation_IoAddressRequiredFormat",
                    "IO“{0}”的 PLC 地址不能为空。",
                    model.SignalKey),
                nameof(model.PlcAddress)));

        if (model.AddressCount <= 0)
            issues.Add(new ValidationIssue(
                _languageService.Format(
                    "Navigation_Hardware_Validation_IoAddressCountPositiveFormat",
                    "IO“{0}”的地址长度必须大于 0。",
                    model.SignalKey),
                nameof(model.AddressCount)));

        if (string.IsNullOrWhiteSpace(model.Category))
            issues.Add(new ValidationIssue(
                _languageService.Format(
                    "Navigation_Hardware_Validation_IoCategoryRequiredFormat",
                    "IO“{0}”的分类不能为空。",
                    model.SignalKey),
                nameof(model.Category)));
        else if (!IoMappingOptionCatalog.IsKnownCategory(model.Category))
            issues.Add(new ValidationIssue(
                _languageService.Format(
                    "Navigation_Hardware_Validation_IoCategoryKnownFormat",
                    "IO“{0}”的分类不在五类 IO 模型内。",
                    model.SignalKey),
                nameof(model.Category)));

        var derivedDirection = IoMappingOptionCatalog.GetDirectionForCategory(model.Category);
        if (!string.IsNullOrWhiteSpace(derivedDirection)
            && !string.Equals(model.Direction, derivedDirection, StringComparison.OrdinalIgnoreCase))
            issues.Add(new ValidationIssue(
                _languageService.Format(
                    "Navigation_Hardware_Validation_IoDirectionByCategoryFormat",
                    "IO“{0}”的方向必须由分类决定，不能手工改成其他方向。",
                    model.SignalKey),
                nameof(model.Direction)));

        return Task.FromResult<IReadOnlyCollection<ValidationIssue>>(issues);
    }
}
