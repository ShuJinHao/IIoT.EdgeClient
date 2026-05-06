using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Application.Modules.Samples;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.Homogenization.Samples;

/// <summary>
/// 匀浆开发样本导入器，只播种样本 PLC 设备；IO 点位统一由硬件模板生成并写入当前 PLC 的硬件配置。
/// </summary>
public sealed class HomogenizationDevelopmentSampleContributor : DevelopmentSampleContributorBase
{
    private const string SeedRemark = "匀浆开发样本";
    private static readonly IReadOnlyList<HomogenizationDeviceSeed> DefaultDevices =
    [
        new()
        {
            DeviceName = "PLC-Homogenization-01",
            DeviceModel = "Mc",
            IpAddress = "127.0.0.1",
            Port1 = 6000,
            ConnectTimeout = 3000,
            IsEnabled = true,
            Remark = "匀浆开发样本 PLC"
        }
    ];

    private readonly ILogService _logger;
    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IRepository<IoMappingEntity> _ioMappings;
    private readonly HomogenizationDeviceSeedOptions _options;

    public HomogenizationDevelopmentSampleContributor(
        IConfiguration configuration,
        IRepository<NetworkDeviceEntity> networkDevices,
        IRepository<IoMappingEntity> ioMappings,
        ILogService logger,
        IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles)
        : base(configuration, hardwareProfiles)
    {
        _networkDevices = networkDevices;
        _ioMappings = ioMappings;
        _logger = logger;
        _options = BindOptions<HomogenizationDeviceSeedOptions>(HomogenizationDeviceSeedOptions.SectionName);
    }

    /// <summary>
    /// 样本导入器归属的匀浆模块标识。
    /// </summary>
    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override bool ShouldEnsureConfigurationSamples()
        => _options.Enabled;

    protected override void OnConfigurationSamplesSkipped()
        => _logger.Info("[匀浆][设备样本] 自动播种已关闭。");

    protected override async Task EnsureConfigurationSamplesCoreAsync(CancellationToken cancellationToken)
    {
        _logger.Info("[匀浆][设备样本] 开始检查样本 PLC 设备和硬件 IO 映射。");

        if (DefaultDevices.Count == 0)
        {
            _logger.Warn("[匀浆][设备样本] 没有可用的默认 PLC 样本。");
            return;
        }

        if (_options.ResetBeforeImport)
        {
            await ResetHomogenizationConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }

        var hardwareProfile = GetHardwareProfile();
        var importedDeviceCount = 0;
        var importedMappingCount = 0;

        foreach (var seedDevice in DefaultDevices)
        {
            var device = await EnsureDeviceAsync(seedDevice, hardwareProfile, cancellationToken).ConfigureAwait(false);
            if (device is null)
            {
                continue;
            }

            importedDeviceCount++;
            importedMappingCount += await EnsureMappingsAsync(device, hardwareProfile, cancellationToken).ConfigureAwait(false);
        }

        _logger.Info($"[匀浆][设备样本] 播种检查完成。设备导入 {importedDeviceCount} 台，IO 映射补齐 {importedMappingCount} 条。");
    }

    private async Task ResetHomogenizationConfigurationAsync(CancellationToken cancellationToken)
    {
        var existingDevices = await _networkDevices.GetListAsync(
            x => x.ModuleId == DependencyInjection.ModuleKey,
            cancellationToken).ConfigureAwait(false);

        if (existingDevices.Count == 0)
        {
            return;
        }

        var deviceIds = existingDevices.Select(x => x.Id).ToHashSet();
        var mappings = deviceIds.Count == 0
            ? []
            : await _ioMappings.GetListAsync(x => deviceIds.Contains(x.NetworkDeviceId), cancellationToken).ConfigureAwait(false);

        foreach (var mapping in mappings)
        {
            _ioMappings.Delete(mapping);
        }

        foreach (var device in existingDevices)
        {
            _networkDevices.Delete(device);
        }

        if (mappings.Count > 0)
        {
            await _ioMappings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await _networkDevices.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info($"[匀浆][设备样本] 已重置 {existingDevices.Count} 台设备和 {mappings.Count} 条映射。");
    }

    private async Task<NetworkDeviceEntity?> EnsureDeviceAsync(
        HomogenizationDeviceSeed seedDevice,
        IModuleHardwareProfileProvider hardwareProfile,
        CancellationToken cancellationToken)
    {
        var existingDevice = await _networkDevices.GetAsync(
            x => x.ModuleId == DependencyInjection.ModuleKey
                && x.DeviceName == seedDevice.DeviceName,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (existingDevice is not null)
        {
            _logger.Info($"[匀浆][设备样本] 设备“{seedDevice.DeviceName}”已存在，跳过设备写入。");
            return existingDevice;
        }

        var conflictingDevice = await _networkDevices.GetAsync(
            x => x.DeviceName == seedDevice.DeviceName,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (conflictingDevice is not null)
        {
            _logger.Warn($"[匀浆][设备样本] 跳过设备“{seedDevice.DeviceName}”，该名称已被模块“{conflictingDevice.ModuleId}”占用。");
            return null;
        }

        var defaults = hardwareProfile.GetDefaultPlcSettings();
        var device = NetworkDeviceEntity.Create(
            seedDevice.DeviceName,
            DeviceType.PLC,
            string.IsNullOrWhiteSpace(seedDevice.IpAddress) ? "127.0.0.1" : seedDevice.IpAddress,
            seedDevice.Port1 > 0 ? seedDevice.Port1 : defaults.Port1 ?? 6000);
        device.AssignModule(
            DependencyInjection.ModuleKey,
            string.IsNullOrWhiteSpace(seedDevice.DeviceModel)
                ? defaults.DeviceModel
                : seedDevice.DeviceModel);
        device.UpdateEndpoint(
            device.IpAddress,
            device.Port1,
            device.Port2,
            seedDevice.ConnectTimeout > 0 ? seedDevice.ConnectTimeout : defaults.ConnectTimeout ?? 3000);
        device.SetEnabled(seedDevice.IsEnabled);
        device.UpdateRemark(string.IsNullOrWhiteSpace(seedDevice.Remark) ? SeedRemark : seedDevice.Remark);

        _networkDevices.Add(device);
        await _networkDevices.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.Info($"[匀浆][设备样本] 已写入设备“{device.DeviceName}”。");
        return device;
    }

    private async Task<int> EnsureMappingsAsync(
        NetworkDeviceEntity device,
        IModuleHardwareProfileProvider hardwareProfile,
        CancellationToken cancellationToken)
    {
        var existingMappings = await _ioMappings.GetListAsync(
            x => x.NetworkDeviceId == device.Id,
            cancellationToken).ConfigureAwait(false);

        var existingBySignalKey = existingMappings
            .GroupBy(static x => x.SignalKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static x => x.Key, static x => x.First(), StringComparer.OrdinalIgnoreCase);

        var addedCount = 0;
        var repairedCount = 0;
        foreach (var template in hardwareProfile.GetDefaultIoTemplate().OrderBy(static x => x.SortOrder))
        {
            if (existingBySignalKey.TryGetValue(template.SignalKey, out var existingMapping))
            {
                if (ApplyTemplateMetadata(existingMapping, template))
                {
                    _ioMappings.Update(existingMapping);
                    repairedCount++;
                }

                continue;
            }

            var entity = IoMappingEntity.Create(
                device.Id,
                template.SignalKey,
                template.PlcAddress,
                template.AddressCount,
                template.DataType,
                template.Direction,
                template.Category,
                template.BusinessGroup,
                template.SignalName);
            entity.UpdateSortOrder(template.SortOrder);
            entity.UpdateMetadata(
                template.SignalKey,
                template.DataType,
                template.Direction,
                template.Category,
                template.BusinessGroup,
                template.SignalName,
                string.IsNullOrWhiteSpace(template.Remark) ? SeedRemark : template.Remark);
            _ioMappings.Add(entity);
            addedCount++;
        }

        if (addedCount == 0 && repairedCount == 0)
        {
            _logger.Info($"[匀浆][设备样本] 设备“{device.DeviceName}”无需补写映射。");
            return 0;
        }

        await _ioMappings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info($"[匀浆][设备样本] 已为“{device.DeviceName}”新增 {addedCount} 条映射，修复 {repairedCount} 条模板元数据。");
        return addedCount + repairedCount;
    }

    private static bool ApplyTemplateMetadata(IoMappingEntity existing, ModuleIoTemplateEntry template)
    {
        var templateRemark = string.IsNullOrWhiteSpace(template.Remark) ? SeedRemark : template.Remark.Trim();
        var changed = existing.AddressCount != template.AddressCount
            || !string.Equals(existing.DataType, template.DataType, StringComparison.Ordinal)
            || !string.Equals(existing.Direction, template.Direction, StringComparison.Ordinal)
            || existing.SortOrder != template.SortOrder
            || !string.Equals(existing.Category, template.Category, StringComparison.Ordinal)
            || !string.Equals(existing.BusinessGroup, template.BusinessGroup, StringComparison.Ordinal)
            || !string.Equals(existing.SignalName, template.SignalName, StringComparison.Ordinal)
            || !string.Equals(existing.Remark, templateRemark, StringComparison.Ordinal);

        existing.UpdateAddress(existing.PlcAddress, template.AddressCount);
        existing.UpdateSortOrder(template.SortOrder);
        existing.UpdateMetadata(
            existing.SignalKey,
            template.DataType,
            template.Direction,
            template.Category,
            template.BusinessGroup,
            template.SignalName,
            templateRemark);

        return changed;
    }

    private IModuleHardwareProfileProvider GetHardwareProfile()
        => GetHardwareProfile($"匀浆设备样本导入需要模块“{DependencyInjection.ModuleKey}”的硬件模板提供器。");

    /// <summary>
    /// 匀浆开发设备样本导入开关。IO 点位不在 JSON 内维护，只由硬件模板补齐。
    /// </summary>
    private sealed class HomogenizationDeviceSeedOptions
    {
        public const string SectionName = "Modules:Homogenization:DeviceSeed";

        /// <summary>
        /// 是否启用匀浆开发样本自动导入。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 导入前是否删除旧的匀浆样本设备和 IO 映射，仅用于开发环境重置样本。
        /// </summary>
        public bool ResetBeforeImport { get; set; }
    }

    /// <summary>
    /// 匀浆样本设备配置，不包含 IO 点位，点位由插件硬件模板补齐。
    /// </summary>
    private sealed class HomogenizationDeviceSeed
    {
        /// <summary>
        /// 样本设备名称，作为设备表唯一识别和 UI 展示名称。
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// 设备型号说明，用于设备资料展示和硬件模板校验。
        /// </summary>
        public string? DeviceModel { get; set; }

        /// <summary>
        /// PLC IP 地址，开发样本默认指向本地或测试 PLC。
        /// </summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>
        /// PLC 通讯端口。
        /// </summary>
        public int Port1 { get; set; }

        /// <summary>
        /// PLC 连接超时时间，单位毫秒。
        /// </summary>
        public int ConnectTimeout { get; set; }

        /// <summary>
        /// 样本设备是否启用。
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 样本设备备注，写入设备资料用于标识开发种子来源。
        /// </summary>
        public string? Remark { get; set; }
    }
}
