using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Samples;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.Stacking.Samples;

/// <summary>
/// 叠片开发样本导入器，只在开发环境按配置写入样本 PLC、IO 映射和运行态电芯数据。
/// </summary>
public sealed class StackingDevelopmentSampleContributor : DevelopmentSampleContributorBase
{
    /// <summary>
    /// 写入样本设备和 IO 映射时使用的备注，便于审核时区分开发样本和现场配置。
    /// </summary>
    private const string SampleRemark = "开发样本初始化";

    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IRepository<IoMappingEntity> _ioMappings;
    private readonly IProductionContextStore _contextStore;
    private readonly ILogService _logger;
    private readonly StackingDevelopmentSampleOptions _options;

    public StackingDevelopmentSampleContributor(
        IConfiguration configuration,
        IRepository<NetworkDeviceEntity> networkDevices,
        IRepository<IoMappingEntity> ioMappings,
        IProductionContextStore contextStore,
        ILogService logger,
        IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles)
        : base(configuration, hardwareProfiles)
    {
        _networkDevices = networkDevices;
        _ioMappings = ioMappings;
        _contextStore = contextStore;
        _logger = logger;
        _options = BindOptions<StackingDevelopmentSampleOptions>(StackingDevelopmentSampleOptions.SectionName);
    }

    /// <summary>
    /// 样本导入器归属的叠片模块标识。
    /// </summary>
    public override string ModuleId => StackingModuleConstants.ModuleId;

    /// <summary>
    /// 配置样本只在开发环境且模块启用时导入，避免污染正式现场配置。
    /// </summary>
    protected override bool ShouldEnsureConfigurationSamples()
        => ShouldSeedStackingSamples();

    /// <summary>
    /// 运行态样本只服务本地演示，真实生产数据必须来自 PLC 采集。
    /// </summary>
    protected override bool ShouldEnsureRuntimeSamples()
        => ShouldSeedStackingSamples();

    protected override async Task EnsureConfigurationSamplesCoreAsync(CancellationToken cancellationToken)
    {
        var existingStackingDevices = await _networkDevices.GetListAsync(
            x => x.DeviceType == DeviceType.PLC && x.ModuleId == StackingModuleConstants.ModuleId,
            cancellationToken).ConfigureAwait(false);

        var sampleDevice = existingStackingDevices.FirstOrDefault(x =>
            string.Equals(x.DeviceName, _options.StackingDeviceName, StringComparison.OrdinalIgnoreCase));

        if (existingStackingDevices.Count > 0 && sampleDevice is null)
        {
            _logger.Info("[开发样本] 已存在叠片 PLC 配置，跳过样本设备初始化。");
            return;
        }

        if (sampleDevice is null)
        {
            var plcDefaults = GetStackingHardwareProfile().GetDefaultPlcSettings();
            var conflictingDevice = await _networkDevices.GetAsync(
                x => x.DeviceName == _options.StackingDeviceName,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (conflictingDevice is not null)
            {
                _logger.Warn(
                    $"[开发样本] 设备名称 '{_options.StackingDeviceName}' 已被模块 '{conflictingDevice.ModuleId}' 使用，跳过叠片样本初始化。");
                return;
            }

            sampleDevice = NetworkDeviceEntity.Create(
                _options.StackingDeviceName,
                DeviceType.PLC,
                _options.StackingIpAddress,
                _options.StackingPort > 0 ? _options.StackingPort : plcDefaults.Port1 ?? 102);
            sampleDevice.AssignModule(
                StackingModuleConstants.ModuleId,
                string.IsNullOrWhiteSpace(_options.StackingPlcModel)
                    ? plcDefaults.DeviceModel
                    : _options.StackingPlcModel);
            sampleDevice.UpdateEndpoint(
                sampleDevice.IpAddress,
                sampleDevice.Port1,
                sampleDevice.Port2,
                _options.StackingConnectTimeout > 0
                    ? _options.StackingConnectTimeout
                    : plcDefaults.ConnectTimeout ?? 3000);
            sampleDevice.Enable();
            sampleDevice.UpdateRemark(SampleRemark);

            _networkDevices.Add(sampleDevice);
            await _networkDevices.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.Info(
                $"[开发样本] 已为模块 {StackingModuleConstants.ModuleId} 写入叠片 PLC 样本设备 '{sampleDevice.DeviceName}'。");
        }

        await EnsureSampleMappingsAsync(sampleDevice, cancellationToken).ConfigureAwait(false);
    }

    protected override async Task EnsureRuntimeSamplesCoreAsync(CancellationToken cancellationToken)
    {
        var sampleDevice = await _networkDevices.GetAsync(
            x => x.DeviceType == DeviceType.PLC
                && x.ModuleId == StackingModuleConstants.ModuleId
                && x.DeviceName == _options.StackingDeviceName,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (sampleDevice is null)
        {
            return;
        }

        var context = _contextStore.GetOrCreate(sampleDevice.DeviceName);
        context.DeviceId = sampleDevice.Id;

        if (!context.CurrentCells.Values.OfType<StackingCellData>().Any())
        {
            var sampleCell = new StackingCellData
            {
                Barcode = _options.SampleBarcode,
                TrayCode = _options.SampleTrayCode,
                LayerCount = _options.SampleLayerCount,
                SequenceNo = 1,
                RuntimeStatus = "开发样本",
                DeviceName = sampleDevice.DeviceName,
                DeviceCode = sampleDevice.DeviceName,
                PlcDeviceId = sampleDevice.Id,
                CellResult = true,
                CompletedTime = DateTime.UtcNow
            };

            context.AddCell(sampleCell.Barcode, sampleCell);
            _logger.Info(
                $"[开发样本] 已为设备 '{sampleDevice.DeviceName}' 写入叠片运行样本电芯 '{sampleCell.Barcode}'。");
        }

        if (!context.Has(StackingModuleConstants.LastPublishedSequenceKey))
        {
            context.Set(StackingModuleConstants.LastPublishedSequenceKey, 1);
        }

        if (!context.Has(StackingModuleConstants.LastPublishedBarcodeKey))
        {
            context.Set(StackingModuleConstants.LastPublishedBarcodeKey, _options.SampleBarcode);
        }
    }

    private async Task EnsureSampleMappingsAsync(
        NetworkDeviceEntity sampleDevice,
        CancellationToken cancellationToken)
    {
        var mappings = await _ioMappings.GetListAsync(
            x => x.NetworkDeviceId == sampleDevice.Id,
            cancellationToken).ConfigureAwait(false);

        var existingLabels = new HashSet<string>(
            mappings.Select(x => x.Label),
            StringComparer.OrdinalIgnoreCase);

        var templateEntries = GetStackingHardwareProfile().GetDefaultIoTemplate();
        var addedCount = 0;
        foreach (var mapping in BuildStackingMappings(sampleDevice.Id, templateEntries, existingLabels))
        {
            _ioMappings.Add(mapping);
            addedCount++;
        }

        if (addedCount == 0)
        {
            return;
        }

        await _ioMappings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info(
            $"[开发样本] 已为设备 '{sampleDevice.DeviceName}' 写入 {addedCount} 条 IO 映射样本。");
    }

    private bool ShouldSeedStackingSamples()
    {
        if (!_options.Enabled || !_options.SeedStackingModule)
        {
            return false;
        }

        if (!string.Equals(GetEnvironmentName(), "Development", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var enabledModules = Configuration
            .GetSection("Modules:Enabled")
            .Get<string[]>()
            ?? [];

        return enabledModules.Contains(StackingModuleConstants.ModuleId, StringComparer.OrdinalIgnoreCase);
    }

    private string GetEnvironmentName()
        => Configuration["Shell:Environment"]?.Trim()
            ?? "Production";

    private IModuleHardwareProfileProvider GetStackingHardwareProfile()
        => GetHardwareProfile(
            $"叠片开发样本初始化需要模块 '{StackingModuleConstants.ModuleId}' 的硬件模板提供者。");

    private static List<IoMappingEntity> BuildStackingMappings(
        int networkDeviceId,
        IReadOnlyCollection<ModuleIoTemplateEntry> templateEntries,
        ISet<string> existingLabels)
    {
        return templateEntries
            .Where(x => !existingLabels.Contains(x.Label))
            .OrderBy(x => x.SortOrder)
            .Select(x =>
            {
                var entity = IoMappingEntity.Create(
                    networkDeviceId,
                    x.Label,
                    x.PlcAddress,
                    x.AddressCount,
                    x.DataType,
                    x.Direction);
                entity.UpdateSortOrder(x.SortOrder);
                entity.UpdateMetadata(
                    x.Label,
                    x.DataType,
                    x.Direction,
                    "单点读数据",
                    string.Empty,
                    string.Empty,
                    SampleRemark);
                return entity;
            })
            .ToList();
    }
}
