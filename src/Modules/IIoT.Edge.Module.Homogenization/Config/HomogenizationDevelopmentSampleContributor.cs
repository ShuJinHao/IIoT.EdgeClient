using IIoT.Edge.Application.Modules.Hardware;
using System.IO;
using System.Text.Json;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Application.Modules.Samples;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.Homogenization.Config;

public sealed class HomogenizationDevelopmentSampleContributor : DevelopmentSampleContributorBase
{
    private const string SeedRemark = "匀浆 IO 种子";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogService _logger;
    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IRepository<IoMappingEntity> _ioMappings;
    private readonly HomogenizationIoSeedOptions _options;

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
        _options = BindOptions<HomogenizationIoSeedOptions>(HomogenizationIoSeedOptions.SectionName);
    }

    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override bool ShouldEnsureConfigurationSamples()
        => _options.Enabled;

    protected override void OnConfigurationSamplesSkipped()
        => _logger.Info("[匀浆][IO种子] 自动播种已关闭。");

    protected override async Task EnsureConfigurationSamplesCoreAsync(CancellationToken cancellationToken)
    {
        _logger.Info("[匀浆][IO种子] 开始检查匀浆设备和 IO 映射。");

        var seedFile = await LoadSeedFileAsync(cancellationToken).ConfigureAwait(false);
        if (seedFile.Devices.Count == 0)
        {
            _logger.Warn("[匀浆][IO种子] 种子文件没有设备配置。");
            return;
        }

        if (_options.ResetBeforeImport)
        {
            await ResetHomogenizationConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }

        var hardwareProfile = GetHardwareProfile();
        var importedDeviceCount = 0;
        var importedMappingCount = 0;

        foreach (var seedDevice in seedFile.Devices)
        {
            ValidateSeedDevice(seedDevice, hardwareProfile);
            var device = await EnsureDeviceAsync(seedDevice, hardwareProfile, cancellationToken).ConfigureAwait(false);
            if (device is null)
            {
                continue;
            }

            importedDeviceCount++;
            importedMappingCount += await EnsureMappingsAsync(device, seedDevice, cancellationToken).ConfigureAwait(false);
        }

        _logger.Info($"[匀浆][IO种子] 播种检查完成。设备导入 {importedDeviceCount} 台，IO 映射导入 {importedMappingCount} 条。");
    }

    private async Task<HomogenizationIoSeedFile> LoadSeedFileAsync(CancellationToken cancellationToken)
    {
        var seedPath = ResolveConfigPath("homogenization.io.seed.json");
        await using var stream = File.OpenRead(seedPath);
        var seedFile = await JsonSerializer.DeserializeAsync<HomogenizationIoSeedFile>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return seedFile ?? new HomogenizationIoSeedFile();
    }

    private static string ResolveConfigPath(string fileName)
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(DependencyInjection).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            var outputPath = Path.Combine(assemblyDirectory, "Config", fileName);
            if (File.Exists(outputPath))
            {
                return outputPath;
            }
        }

        throw new FileNotFoundException($"未找到匀浆模块配置文件：{fileName}。");
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
        _logger.Info($"[匀浆][IO种子] 已重置 {existingDevices.Count} 台设备和 {mappings.Count} 条映射。");
    }

    private async Task<NetworkDeviceEntity?> EnsureDeviceAsync(
        HomogenizationIoSeedDevice seedDevice,
        IModuleHardwareProfileProvider hardwareProfile,
        CancellationToken cancellationToken)
    {
        var existingDevice = await _networkDevices.GetAsync(
            x => x.ModuleId == DependencyInjection.ModuleKey
                && x.DeviceName == seedDevice.DeviceName,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (existingDevice is not null)
        {
            _logger.Info($"[匀浆][IO种子] 设备“{seedDevice.DeviceName}”已存在，跳过设备写入。");
            return existingDevice;
        }

        var conflictingDevice = await _networkDevices.GetAsync(
            x => x.DeviceName == seedDevice.DeviceName,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (conflictingDevice is not null)
        {
            _logger.Warn($"[匀浆][IO种子] 跳过设备“{seedDevice.DeviceName}”，该名称已被模块“{conflictingDevice.ModuleId}”占用。");
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

        _logger.Info($"[匀浆][IO种子] 已写入设备“{device.DeviceName}”。");
        return device;
    }

    private async Task<int> EnsureMappingsAsync(
        NetworkDeviceEntity device,
        HomogenizationIoSeedDevice seedDevice,
        CancellationToken cancellationToken)
    {
        var existingMappings = await _ioMappings.GetListAsync(
            x => x.NetworkDeviceId == device.Id,
            cancellationToken).ConfigureAwait(false);

        var existingByLabel = existingMappings
            .GroupBy(static x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static x => x.Key, static x => x.First(), StringComparer.OrdinalIgnoreCase);

        var addedCount = 0;
        var repairedCount = 0;
        foreach (var mapping in seedDevice.Mappings.OrderBy(static x => x.SortOrder))
        {
            if (existingByLabel.TryGetValue(mapping.Label, out var existingMapping))
            {
                if (ApplySeedMetadata(existingMapping, mapping))
                {
                    _ioMappings.Update(existingMapping);
                    repairedCount++;
                }

                continue;
            }

            var entity = IoMappingEntity.Create(
                device.Id,
                mapping.Label,
                mapping.PlcAddress,
                mapping.AddressCount,
                mapping.DataType,
                mapping.Direction,
                mapping.Category,
                mapping.GroupName,
                mapping.DisplayRole);
            entity.UpdateSortOrder(mapping.SortOrder);
            entity.UpdateMetadata(
                mapping.Label,
                mapping.DataType,
                mapping.Direction,
                mapping.Category,
                mapping.GroupName,
                mapping.DisplayRole,
                string.IsNullOrWhiteSpace(mapping.Remark) ? SeedRemark : mapping.Remark);
            _ioMappings.Add(entity);
            addedCount++;
        }

        if (addedCount == 0 && repairedCount == 0)
        {
            _logger.Info($"[匀浆][IO种子] 设备“{device.DeviceName}”无需补写映射。");
            return 0;
        }

        await _ioMappings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info($"[匀浆][IO种子] 已为“{device.DeviceName}”写入 {addedCount} 条映射，修复 {repairedCount} 条分类字段。");
        return addedCount + repairedCount;
    }

    private static bool ApplySeedMetadata(
        IoMappingEntity existing,
        HomogenizationIoSeedMapping seed)
    {
        var changed = false;

        var category = string.IsNullOrWhiteSpace(seed.Category) ? "单点读数据" : seed.Category.Trim();
        var groupName = seed.GroupName?.Trim() ?? string.Empty;
        var displayRole = seed.DisplayRole?.Trim() ?? string.Empty;
        var seedRemark = string.IsNullOrWhiteSpace(seed.Remark) ? SeedRemark : seed.Remark.Trim();
        changed = existing.SortOrder != seed.SortOrder
            || !string.Equals(existing.Category, category, StringComparison.Ordinal)
            || !string.Equals(existing.GroupName, groupName, StringComparison.Ordinal)
            || !string.Equals(existing.DisplayRole, displayRole, StringComparison.Ordinal)
            || !string.Equals(existing.Remark, seedRemark, StringComparison.Ordinal);

        existing.UpdateSortOrder(seed.SortOrder);
        existing.UpdateMetadata(
            existing.Label,
            existing.DataType,
            existing.Direction,
            category,
            groupName,
            displayRole,
            seedRemark);

        return changed;
    }

    private void ValidateSeedDevice(
        HomogenizationIoSeedDevice seedDevice,
        IModuleHardwareProfileProvider hardwareProfile)
    {
        var mappings = seedDevice.Mappings
            .Select(static mapping => new ModuleIoSnapshot(
                mapping.Label,
                mapping.PlcAddress,
                mapping.AddressCount,
                mapping.DataType,
                mapping.Direction,
                mapping.SortOrder,
                mapping.Category,
                mapping.GroupName,
                mapping.DisplayRole))
            .ToArray();

        var validation = hardwareProfile.ValidatePlcConfiguration(
            seedDevice.DeviceName,
            seedDevice.DeviceModel,
            mappings);

        if (validation.IsValid)
        {
            return;
        }

        var details = string.Join("；", validation.Issues.Select(static issue => issue.Message));
        throw new InvalidOperationException($"匀浆 IO 种子配置无效：{details}");
    }

    private IModuleHardwareProfileProvider GetHardwareProfile()
        => GetHardwareProfile($"匀浆 IO 种子导入需要模块“{DependencyInjection.ModuleKey}”的硬件模板提供器。");

    private sealed class HomogenizationIoSeedOptions
    {
        public const string SectionName = "Modules:Homogenization:IoSeed";

        public bool Enabled { get; set; }

        public bool ResetBeforeImport { get; set; }
    }

    private sealed class HomogenizationIoSeedFile
    {
        public List<HomogenizationIoSeedDevice> Devices { get; set; } = [];
    }

    private sealed class HomogenizationIoSeedDevice
    {
        public string DeviceName { get; set; } = string.Empty;

        public string? DeviceModel { get; set; }

        public string IpAddress { get; set; } = string.Empty;

        public int Port1 { get; set; }

        public int ConnectTimeout { get; set; }

        public bool IsEnabled { get; set; } = true;

        public string? Remark { get; set; }

        public List<HomogenizationIoSeedMapping> Mappings { get; set; } = [];
    }

    private sealed class HomogenizationIoSeedMapping
    {
        public string Label { get; set; } = string.Empty;

        public string PlcAddress { get; set; } = string.Empty;

        public int AddressCount { get; set; } = 1;

        public string DataType { get; set; } = "Int16";

        public string Direction { get; set; } = "Read";

        public string Category { get; set; } = "单点读数据";

        public string GroupName { get; set; } = string.Empty;

        public string DisplayRole { get; set; } = string.Empty;

        public int SortOrder { get; set; }

        public string? Remark { get; set; }
    }
}
