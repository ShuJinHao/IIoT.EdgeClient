using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Diagnostics;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Host.Bootstrap.Modules;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Shell.Core;

internal sealed class StartupPlcConfigurationValidator(
    IRepository<IoMappingEntity> ioMappings,
    ICellDataRegistry cellDataRegistry,
    IStationRuntimeRegistry runtimeRegistry)
    : IStartupAsyncDiagnosticValidator
{
    public async Task ValidateAsync(
        StartupValidationContext context,
        List<StartupDiagnosticIssue> issues,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<DeviceModuleBindingSnapshot>(context.PlcDevices.Count);

        foreach (var device in context.PlcDevices)
        {
            var deviceName = string.IsNullOrWhiteSpace(device.DeviceName) ? $"Id={device.Id}" : device.DeviceName;
            var mappings = await ioMappings.GetListAsync(
                x => x.NetworkDeviceId == device.Id,
                cancellationToken).ConfigureAwait(false);
            var moduleExists = !string.IsNullOrWhiteSpace(device.ModuleId)
                && context.DiscoveredModulesById.ContainsKey(device.ModuleId);
            var moduleEnabled = !string.IsNullOrWhiteSpace(device.ModuleId)
                && context.ModulesById.ContainsKey(device.ModuleId);

            snapshots.Add(new DeviceModuleBindingSnapshot(
                deviceName,
                device.ModuleId,
                moduleExists,
                moduleEnabled,
                mappings.Count > 0));

            ValidateDeviceModuleBinding(context, device, deviceName, mappings, moduleExists, moduleEnabled, issues);
            ValidateHardwareProfile(context, device, deviceName, mappings, issues);
        }

        context.DeviceBindings = snapshots;
    }

    private void ValidateDeviceModuleBinding(
        StartupValidationContext context,
        NetworkDeviceEntity device,
        string deviceName,
        IReadOnlyCollection<IoMappingEntity> mappings,
        bool moduleExists,
        bool moduleEnabled,
        List<StartupDiagnosticIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(device.DeviceName))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("DEVICE_MODULE_MISMATCH", "已启用的 PLC 设备缺少设备名称。", device.DeviceName, deviceName));
        }

        if (string.IsNullOrWhiteSpace(device.ModuleId))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("DEVICE_MODULE_MISMATCH", $"PLC“{deviceName}”缺少 ModuleId。", device.ModuleId, deviceName));
        }
        else if (!moduleExists)
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("DEVICE_MODULE_MISMATCH", $"PLC“{deviceName}”引用了未知模块“{device.ModuleId}”。", device.ModuleId, deviceName));
        }
        else if (!moduleEnabled)
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("MODULE_NOT_ENABLED", $"PLC“{deviceName}”引用模块“{device.ModuleId}”，但该模块未启用。", device.ModuleId, deviceName));
        }
        else
        {
            ValidateEnabledModuleServices(context, device, deviceName, issues);
        }

        ValidateDeviceEndpoint(device, deviceName, issues);
        ValidateIoMappings(device, deviceName, mappings, issues);
    }

    private void ValidateEnabledModuleServices(
        StartupValidationContext context,
        NetworkDeviceEntity device,
        string deviceName,
        List<StartupDiagnosticIssue> issues)
    {
        var module = context.ModulesById[device.ModuleId];
        if (!runtimeRegistry.HasFactory(module.ModuleId))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("RUNTIME_FACTORY_MISSING", $"PLC“{deviceName}”使用模块“{module.ModuleId}”，但运行时工厂未注册。", module.ModuleId, deviceName));
        }

        if (!cellDataRegistry.IsRegistered(module.ProcessType))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CELLDATA_REGISTRATION_MISSING", $"PLC“{deviceName}”使用模块“{module.ModuleId}”，但 CellData 未注册。", module.ModuleId, deviceName));
        }
    }

    private static void ValidateDeviceEndpoint(
        NetworkDeviceEntity device,
        string deviceName,
        List<StartupDiagnosticIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(device.DeviceModel)
            || !Enum.TryParse<PlcType>(device.DeviceModel, ignoreCase: true, out var plcType))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("DEVICE_MODEL_INVALID", $"PLC“{deviceName}”的 DeviceModel 无效：{device.DeviceModel ?? "<空>"}。", device.ModuleId, deviceName));
            return;
        }

        if (plcType == PlcType.ModbusRtu)
        {
            if (string.IsNullOrWhiteSpace(device.SendCmd1))
            {
                issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"PLC“{deviceName}”是 Modbus RTU 时，Command1 必须填写串口设备名称。", device.ModuleId, deviceName));
            }

            if (device.Port1 is < 1 or > 247)
            {
                issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"PLC“{deviceName}”是 Modbus RTU 时，Port1 必须填写 1 到 247 之间的从站 ID。", device.ModuleId, deviceName));
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(device.IpAddress))
            {
                issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"PLC“{deviceName}”缺少 IpAddress。", device.ModuleId, deviceName));
            }

            if (device.Port1 <= 0 || device.Port1 > 65535)
            {
                issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"PLC“{deviceName}”的 Port1 无效：{device.Port1}。", device.ModuleId, deviceName));
            }
        }

        if (device.ConnectTimeout <= 0)
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("CONFIG_INVALID", $"PLC“{deviceName}”的 ConnectTimeout 必须大于 0。", device.ModuleId, deviceName));
        }
    }

    private static void ValidateIoMappings(
        NetworkDeviceEntity device,
        string deviceName,
        IReadOnlyCollection<IoMappingEntity> mappings,
        List<StartupDiagnosticIssue> issues)
    {
        if (mappings.Count == 0)
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("DEVICE_MODULE_MISMATCH", $"PLC“{deviceName}”没有配置 IO 映射。", device.ModuleId, deviceName));
            return;
        }

        if (mappings.Any(x => string.IsNullOrWhiteSpace(x.PlcAddress)))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("DEVICE_MODULE_MISMATCH", $"PLC“{deviceName}”存在 PlcAddress 为空的 IO 映射。", device.ModuleId, deviceName));
        }

        if (mappings.Any(x => x.AddressCount <= 0))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("DEVICE_MODULE_MISMATCH", $"PLC“{deviceName}”存在 AddressCount 小于等于 0 的 IO 映射。", device.ModuleId, deviceName));
        }

        if (mappings.Any(x => x.Direction is not ("Read" or "Write")))
        {
            issues.Add(StartupDiagnosticIssueFactory.Create("DEVICE_MODULE_MISMATCH", $"PLC“{deviceName}”存在 Direction 无效的 IO 映射。", device.ModuleId, deviceName));
        }
    }

    private static void ValidateHardwareProfile(
        StartupValidationContext context,
        NetworkDeviceEntity device,
        string deviceName,
        IReadOnlyCollection<IoMappingEntity> mappings,
        List<StartupDiagnosticIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(device.ModuleId)
            || !context.HardwareProfilesByModuleId.TryGetValue(device.ModuleId, out var provider))
        {
            return;
        }

        var validationResult = provider.ValidatePlcConfiguration(
            deviceName,
            device.DeviceModel,
            mappings.Select(static x => new ModuleIoSnapshot(
                    x.SignalKey,
                    x.PlcAddress,
                    x.AddressCount,
                    x.DataType,
                    x.Direction,
                    x.SortOrder,
                    x.Category,
                    x.BusinessGroup,
                    x.SignalName))
                .ToArray());

        if (!validationResult.IsValid)
        {
            issues.AddRange(validationResult.Issues.Select(issue =>
                StartupDiagnosticIssueFactory.Create("HARDWARE_PROFILE_INVALID", issue.Message, device.ModuleId, deviceName)));
        }
    }
}
