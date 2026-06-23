using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Application.Modules.Samples;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.DieCutting.Samples;

/// <summary>
/// 模切开发样本导入器，播种 24 台老设备 PLC 样本和标准只读 IO 映射模板。
/// </summary>
public sealed class DieCuttingDevelopmentSampleContributor : DevelopmentSampleContributorBase
{
    private const string SeedRemark = "模切只读采集开发样本";

    private readonly ILogService _logger;
    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IRepository<IoMappingEntity> _ioMappings;
    private readonly DieCuttingDeviceSeedOptions _options;

    public DieCuttingDevelopmentSampleContributor(
        IConfiguration configuration,
        IRepository<NetworkDeviceEntity> networkDevices,
        IRepository<IoMappingEntity> ioMappings,
        ILogService logger,
        IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles,
        EdgeRuntimePaths? runtimePaths = null)
        : base(configuration, hardwareProfiles)
    {
        _networkDevices = networkDevices;
        _ioMappings = ioMappings;
        _logger = logger;
        _options = BindOptions<DieCuttingDeviceSeedOptions>($"Modules:{DependencyInjection.ModuleKey}:DeviceSeed");
    }

    /// <summary>
    /// 样本导入器归属的模切模块标识。
    /// </summary>
    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override bool ShouldEnsureConfigurationSamples()
        => _options.Enabled;

    protected override void OnConfigurationSamplesSkipped()
        => _logger.Info("[模切][设备样本] 自动播种已关闭。");

    protected override async Task EnsureConfigurationSamplesCoreAsync(CancellationToken cancellationToken)
    {
        _logger.Info("[模切][设备样本] 开始检查 24 台模切 PLC 设备和只读 IO 映射。");

        if (_options.ResetBeforeImport)
        {
            await ResetDieCuttingConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }

        var hardwareProfile = GetHardwareProfile();
        var importedDeviceCount = 0;
        var importedMappingCount = 0;

        foreach (var seedDevice in BuildDefaultDevices())
        {
            var device = await EnsureDeviceAsync(seedDevice, hardwareProfile, cancellationToken).ConfigureAwait(false);
            importedDeviceCount++;
            importedMappingCount += await EnsureMappingsAsync(device, hardwareProfile, cancellationToken).ConfigureAwait(false);
        }

        _logger.Info($"[模切][设备样本] 播种检查完成。设备 {importedDeviceCount} 台，新增 IO 映射 {importedMappingCount} 条。");
    }

    private async Task ResetDieCuttingConfigurationAsync(CancellationToken cancellationToken)
    {
        var seedNames = BuildDefaultDevices()
            .Select(static x => x.DeviceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingDevices = await _networkDevices.GetListAsync(_ => true, cancellationToken).ConfigureAwait(false);
        var targetDevices = existingDevices
            .Where(device => seedNames.Contains(device.DeviceName)
                             || string.Equals(device.Remark, SeedRemark, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (targetDevices.Length == 0)
        {
            return;
        }

        var deviceIds = targetDevices.Select(static x => x.Id).ToHashSet();
        var mappings = await _ioMappings
            .GetListAsync(x => deviceIds.Contains(x.NetworkDeviceId), cancellationToken)
            .ConfigureAwait(false);

        foreach (var mapping in mappings)
        {
            _ioMappings.Delete(mapping);
        }

        foreach (var device in targetDevices)
        {
            _networkDevices.Delete(device);
        }

        if (mappings.Count > 0)
        {
            await _ioMappings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await _networkDevices.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info($"[模切][设备样本] 已重置 {targetDevices.Length} 台模切样本设备和 {mappings.Count} 条映射。");
    }

    private async Task<NetworkDeviceEntity> EnsureDeviceAsync(
        DieCuttingDeviceSeed seedDevice,
        IModuleHardwareProfileProvider hardwareProfile,
        CancellationToken cancellationToken)
    {
        var existingDevice = await _networkDevices.GetAsync(
            x => x.DeviceName == seedDevice.DeviceName,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (existingDevice is not null)
        {
            _logger.Info($"[模切][设备样本] 设备“{seedDevice.DeviceName}”已存在，跳过设备写入。");
            return existingDevice;
        }

        var defaults = hardwareProfile.GetDefaultPlcSettings();
        var device = NetworkDeviceEntity.Create(
            seedDevice.DeviceName,
            DeviceType.PLC,
            seedDevice.IpAddress,
            seedDevice.Port1 > 0 ? seedDevice.Port1 : defaults.Port1 ?? 65530);
        device.UpdateDeviceModel(string.IsNullOrWhiteSpace(seedDevice.DeviceModel)
            ? defaults.DeviceModel
            : seedDevice.DeviceModel);
        device.UpdateEndpoint(
            device.IpAddress,
            device.Port1,
            device.Port2,
            seedDevice.ConnectTimeout > 0 ? seedDevice.ConnectTimeout : defaults.ConnectTimeout ?? 3000);
        device.SetEnabled(seedDevice.IsEnabled);
        device.UpdateRemark(SeedRemark);

        _networkDevices.Add(device);
        await _networkDevices.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.Info($"[模切][设备样本] 已写入设备“{device.DeviceName}”。");
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
        if (existingMappings.Count > 0)
        {
            _logger.Info($"[模切][设备样本] 设备“{device.DeviceName}”已有 IO 映射，跳过标准点位播种。");
            return 0;
        }

        var templates = hardwareProfile.GetDefaultIoTemplate()
            .Where(static x => !string.IsNullOrWhiteSpace(x.PlcAddress))
            .OrderBy(static x => x.SortOrder)
            .ThenBy(static x => x.Direction, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static x => x.SignalKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var template in templates)
        {
            _ioMappings.Add(CreateMappingFromTemplate(device.Id, template));
        }

        await _ioMappings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info($"[模切][设备样本] 设备“{device.DeviceName}”首次初始化 IO 映射，已播种 {templates.Length} 条标准点位。");
        return templates.Length;
    }

    private static IoMappingEntity CreateMappingFromTemplate(int networkDeviceId, ModuleIoTemplateEntry template)
    {
        var entity = IoMappingEntity.Create(
            networkDeviceId,
            template.SignalKey,
            template.PlcAddress,
            template.AddressCount,
            template.DataType,
            template.Direction,
            template.Category,
            template.BusinessGroup);
        entity.UpdateSortOrder(template.SortOrder);
        entity.UpdateMetadata(
            template.SignalKey,
            template.DataType,
            template.Direction,
            template.Category,
            template.BusinessGroup,
            string.IsNullOrWhiteSpace(template.Remark) ? SeedRemark : template.Remark);
        return entity;
    }

    private IModuleHardwareProfileProvider GetHardwareProfile()
        => GetHardwareProfile($"模切设备样本导入需要模块“{DependencyInjection.ModuleKey}”的硬件模板提供器。");

    private static IReadOnlyList<DieCuttingDeviceSeed> BuildDefaultDevices()
        =>
        [
            .. Enumerable.Range(1, 12)
                .Select(index => new DieCuttingDeviceSeed
                {
                    DeviceName = $"P1-AP{index:D2}",
                    IpAddress = $"10.110.0.{10 + index}",
                    Remark = "负极模切 AP"
                }),
            .. Enumerable.Range(1, 12)
                .Select(index => new DieCuttingDeviceSeed
                {
                    DeviceName = $"P2-CP{index:D2}",
                    IpAddress = $"10.110.1.{10 + index}",
                    Remark = "正极模切 CP"
                })
        ];

    /// <summary>
    /// 模切设备样本导入开关。
    /// </summary>
    private sealed class DieCuttingDeviceSeedOptions
    {
        /// <summary>
        /// 是否启用模切开发样本自动导入。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 导入前是否删除旧的模切样本设备和 IO 映射。
        /// </summary>
        public bool ResetBeforeImport { get; set; }
    }

    /// <summary>
    /// 模切样本 PLC 设备配置。
    /// </summary>
    private sealed class DieCuttingDeviceSeed
    {
        /// <summary>
        /// 样本 PLC 设备名。
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// PLC 设备型号。
        /// </summary>
        public string DeviceModel { get; set; } = "Mc";

        /// <summary>
        /// PLC IP 地址。
        /// </summary>
        public string IpAddress { get; set; } = string.Empty;

        /// <summary>
        /// PLC 通讯端口。
        /// </summary>
        public int Port1 { get; set; } = 65530;

        /// <summary>
        /// PLC 连接超时时间。
        /// </summary>
        public int ConnectTimeout { get; set; } = 3000;

        /// <summary>
        /// 样本设备是否启用。
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 样本设备所属工序备注。
        /// </summary>
        public string? Remark { get; set; }
    }
}
