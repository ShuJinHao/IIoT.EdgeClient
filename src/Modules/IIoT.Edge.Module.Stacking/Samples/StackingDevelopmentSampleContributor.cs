using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.Stacking.Samples;

public sealed class StackingDevelopmentSampleContributor : IDevelopmentSampleContributor
{
    private const string SampleRemark = "Development sample bootstrap";

    private readonly IConfiguration _configuration;
    private readonly IRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IRepository<IoMappingEntity> _ioMappings;
    private readonly IProductionContextStore _contextStore;
    private readonly ILogService _logger;
    private readonly Dictionary<string, IModuleHardwareProfileProvider> _hardwareProfiles;
    private readonly StackingDevelopmentSampleOptions _options = new();

    public StackingDevelopmentSampleContributor(
        IConfiguration configuration,
        IRepository<NetworkDeviceEntity> networkDevices,
        IRepository<IoMappingEntity> ioMappings,
        IProductionContextStore contextStore,
        ILogService logger,
        IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles)
    {
        _configuration = configuration;
        _networkDevices = networkDevices;
        _ioMappings = ioMappings;
        _contextStore = contextStore;
        _logger = logger;
        _hardwareProfiles = hardwareProfiles.ToDictionary(x => x.ModuleId, StringComparer.OrdinalIgnoreCase);

        configuration.GetSection(StackingDevelopmentSampleOptions.SectionName).Bind(_options);
    }

    public string ModuleId => StackingModuleConstants.ModuleId;

    public async Task EnsureConfigurationSamplesAsync(CancellationToken cancellationToken = default)
    {
        if (!ShouldSeedStackingSamples())
        {
            return;
        }

        var existingStackingDevices = await _networkDevices.GetListAsync(
            x => x.DeviceType == DeviceType.PLC && x.ModuleId == StackingModuleConstants.ModuleId,
            cancellationToken).ConfigureAwait(false);

        var sampleDevice = existingStackingDevices.FirstOrDefault(x =>
            string.Equals(x.DeviceName, _options.StackingDeviceName, StringComparison.OrdinalIgnoreCase));

        if (existingStackingDevices.Count > 0 && sampleDevice is null)
        {
            _logger.Info("[DevSamples] Existing Stacking PLC configuration detected. Skip sample device bootstrap.");
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
                    $"[DevSamples] Skip Stacking sample bootstrap because device name '{_options.StackingDeviceName}' is already occupied by module '{conflictingDevice.ModuleId}'.");
                return;
            }

            sampleDevice = new NetworkDeviceEntity(
                _options.StackingDeviceName,
                DeviceType.PLC,
                _options.StackingIpAddress,
                _options.StackingPort > 0 ? _options.StackingPort : plcDefaults.Port1 ?? 102)
            {
                DeviceModel = string.IsNullOrWhiteSpace(_options.StackingPlcModel)
                    ? plcDefaults.DeviceModel
                    : _options.StackingPlcModel,
                ModuleId = StackingModuleConstants.ModuleId,
                ConnectTimeout = _options.StackingConnectTimeout > 0
                    ? _options.StackingConnectTimeout
                    : plcDefaults.ConnectTimeout ?? 3000,
                IsEnabled = true,
                Remark = SampleRemark
            };

            _networkDevices.Add(sampleDevice);
            await _networkDevices.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _logger.Info(
                $"[DevSamples] Seeded development PLC device '{sampleDevice.DeviceName}' for module {StackingModuleConstants.ModuleId}.");
        }

        await EnsureSampleMappingsAsync(sampleDevice, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureRuntimeSamplesAsync(CancellationToken cancellationToken = default)
    {
        if (!ShouldSeedStackingSamples())
        {
            return;
        }

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
                RuntimeStatus = "DevelopmentSample",
                DeviceName = sampleDevice.DeviceName,
                DeviceCode = sampleDevice.DeviceName,
                PlcDeviceId = sampleDevice.Id,
                CellResult = true,
                CompletedTime = DateTime.UtcNow
            };

            context.AddCell(sampleCell.Barcode, sampleCell);
            _logger.Info(
                $"[DevSamples] Seeded Stacking runtime sample cell '{sampleCell.Barcode}' for '{sampleDevice.DeviceName}'.");
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
            $"[DevSamples] Seeded {addedCount} IO mappings for '{sampleDevice.DeviceName}'.");
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

        var enabledModules = _configuration
            .GetSection("Modules:Enabled")
            .Get<string[]>()
            ?? [];

        return enabledModules.Contains(StackingModuleConstants.ModuleId, StringComparer.OrdinalIgnoreCase);
    }

    private string GetEnvironmentName()
        => _configuration["Shell:Environment"]?.Trim()
            ?? "Production";

    private IModuleHardwareProfileProvider GetStackingHardwareProfile()
    {
        if (_hardwareProfiles.TryGetValue(StackingModuleConstants.ModuleId, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException(
            $"Development sample bootstrap requires a hardware profile provider for module '{StackingModuleConstants.ModuleId}'.");
    }

    private static List<IoMappingEntity> BuildStackingMappings(
        int networkDeviceId,
        IReadOnlyCollection<ModuleIoTemplateEntry> templateEntries,
        ISet<string> existingLabels)
    {
        return templateEntries
            .Where(x => !existingLabels.Contains(x.Label))
            .OrderBy(x => x.SortOrder)
            .Select(x => new IoMappingEntity(
                networkDeviceId,
                x.Label,
                x.PlcAddress,
                x.AddressCount,
                x.DataType,
                x.Direction)
            {
                SortOrder = x.SortOrder,
                Remark = SampleRemark
            })
            .ToList();
    }
}
