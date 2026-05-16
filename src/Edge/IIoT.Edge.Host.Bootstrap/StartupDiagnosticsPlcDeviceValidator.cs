using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Host.Bootstrap;

public interface IStartupDiagnosticsPlcDeviceValidator
{
    void ValidateDeviceEndpoint(
        NetworkDeviceEntity device,
        string deviceName,
        List<StartupDiagnosticIssue> issues);

    void ValidateIoMappings(
        NetworkDeviceEntity device,
        string deviceName,
        IReadOnlyCollection<IoMappingEntity> mappings,
        List<StartupDiagnosticIssue> issues);
}

internal sealed class StartupDiagnosticsPlcDeviceValidator : IStartupDiagnosticsPlcDeviceValidator
{
    public void ValidateDeviceEndpoint(
        NetworkDeviceEntity device,
        string deviceName,
        List<StartupDiagnosticIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(device.DeviceModel)
            || !Enum.TryParse<PlcType>(device.DeviceModel, ignoreCase: true, out var plcType))
        {
            issues.Add(CreateIssue("DEVICE_MODEL_INVALID", $"PLC“{deviceName}”的 DeviceModel 无效：{device.DeviceModel ?? "<空>"}。", device.ModuleId, deviceName));
            return;
        }

        if (plcType == PlcType.ModbusRtu)
        {
            if (string.IsNullOrWhiteSpace(device.SendCmd1))
            {
                issues.Add(CreateIssue("CONFIG_INVALID", $"PLC“{deviceName}”是 Modbus RTU 时，Command1 必须填写串口设备名称。", device.ModuleId, deviceName));
            }

            if (device.Port1 is < 1 or > 247)
            {
                issues.Add(CreateIssue("CONFIG_INVALID", $"PLC“{deviceName}”是 Modbus RTU 时，Port1 必须填写 1 到 247 之间的从站 ID。", device.ModuleId, deviceName));
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(device.IpAddress))
            {
                issues.Add(CreateIssue("CONFIG_INVALID", $"PLC“{deviceName}”缺少 IpAddress。", device.ModuleId, deviceName));
            }

            if (device.Port1 <= 0 || device.Port1 > 65535)
            {
                issues.Add(CreateIssue("CONFIG_INVALID", $"PLC“{deviceName}”的 Port1 无效：{device.Port1}。", device.ModuleId, deviceName));
            }
        }

        if (device.ConnectTimeout <= 0)
        {
            issues.Add(CreateIssue("CONFIG_INVALID", $"PLC“{deviceName}”的 ConnectTimeout 必须大于 0。", device.ModuleId, deviceName));
        }
    }

    public void ValidateIoMappings(
        NetworkDeviceEntity device,
        string deviceName,
        IReadOnlyCollection<IoMappingEntity> mappings,
        List<StartupDiagnosticIssue> issues)
    {
        if (mappings.Count == 0)
        {
            issues.Add(CreateIssue("DEVICE_MODULE_MISMATCH", $"PLC“{deviceName}”没有配置 IO 映射。", device.ModuleId, deviceName));
            return;
        }

        if (mappings.Any(x => string.IsNullOrWhiteSpace(x.PlcAddress)))
        {
            issues.Add(CreateIssue("DEVICE_MODULE_MISMATCH", $"PLC“{deviceName}”存在 PlcAddress 为空的 IO 映射。", device.ModuleId, deviceName));
        }

        if (mappings.Any(x => x.AddressCount <= 0))
        {
            issues.Add(CreateIssue("DEVICE_MODULE_MISMATCH", $"PLC“{deviceName}”存在 AddressCount 小于等于 0 的 IO 映射。", device.ModuleId, deviceName));
        }

        if (mappings.Any(x => x.Direction is not ("Read" or "Write")))
        {
            issues.Add(CreateIssue("DEVICE_MODULE_MISMATCH", $"PLC“{deviceName}”存在 Direction 无效的 IO 映射。", device.ModuleId, deviceName));
        }
    }



    private static StartupDiagnosticIssue CreateIssue(
        string code,
        string message,
        string? moduleId = null,
        string? deviceName = null)
        => new(code, message, moduleId, deviceName);
}
