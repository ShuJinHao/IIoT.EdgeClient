using AutoMapper;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Mappings.Cloud.Stacking;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.SharedKernel.Modules.Stacking;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Infrastructure.Integration.PassStation;

public sealed class StackingCloudUploader : IProcessCloudUploader
{
    private readonly ICloudHttpClient _cloudHttp;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly IMapper _mapper;
    private readonly ILogService _logger;
    private readonly IConfiguration _configuration;
    private readonly IProductionContextStore _contextStore;

    public StackingCloudUploader(
        ICloudHttpClient cloudHttp,
        ICloudApiEndpointProvider endpointProvider,
        IMapper mapper,
        ILogService logger,
        IConfiguration configuration,
        IProductionContextStore contextStore)
    {
        _cloudHttp = cloudHttp;
        _endpointProvider = endpointProvider;
        _mapper = mapper;
        _logger = logger;
        _configuration = configuration;
        _contextStore = contextStore;
    }

    public string ProcessType => StackingModuleConstants.ProcessType;

    public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

    public async Task<bool> UploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return true;
        }

        var isEnabled = _configuration.GetValue<bool>("Modules:Stacking:CloudUploadEnabled");
        if (!isEnabled)
        {
            var deviceName = ResolveDeviceName(records[0], context);
            const string errorMessage = "Stacking cloud upload is disabled by configuration.";
            UpdateDiagnostics(deviceName, false, StackingModuleConstants.CloudUploadDisabledStatus, errorMessage);
            _logger.Warn($"[Cloud] {errorMessage}");
            return false;
        }

        foreach (var record in records)
        {
            if (record.CellData is not StackingCellData stacking)
            {
                var deviceName = ResolveDeviceName(record, context);
                var errorMessage =
                    $"Stacking uploader received unexpected process type '{record.CellData.ProcessType}'.";
                UpdateDiagnostics(deviceName, true, StackingModuleConstants.CloudUploadFailedStatus, errorMessage);
                _logger.Error($"[Cloud] {errorMessage}");
                return false;
            }

            var payload = new
            {
                deviceId = context.Device.DeviceId,
                item = _mapper.Map<StackingCloudDto>(stacking)
            };

            var success = await _cloudHttp.PostAsync(
                _endpointProvider.GetPassStationStackingPath(),
                payload).ConfigureAwait(false);

            if (!success)
            {
                var errorMessage =
                    $"Cloud API returned failure for Stacking barcode '{stacking.Barcode}'.";
                UpdateDiagnostics(
                    ResolveDeviceName(record, context),
                    true,
                    StackingModuleConstants.CloudUploadFailedStatus,
                    errorMessage);
                _logger.Error($"[Cloud] {errorMessage}");
                return false;
            }

            UpdateDiagnostics(
                ResolveDeviceName(record, context),
                true,
                StackingModuleConstants.CloudUploadSuccessStatus,
                errorMessage: null);
        }

        return true;
    }

    private string ResolveDeviceName(CellCompletedRecord record, ProcessCloudUploadContext context)
        => string.IsNullOrWhiteSpace(record.CellData.DeviceName)
            ? context.Device.DeviceName
            : record.CellData.DeviceName;

    private void UpdateDiagnostics(
        string deviceName,
        bool enabled,
        string status,
        string? errorMessage)
    {
        var productionContext = _contextStore.GetOrCreate(deviceName);
        productionContext.Set(StackingModuleConstants.CloudUploadEnabledKey, enabled);
        productionContext.Set(StackingModuleConstants.LastCloudUploadStatusKey, status);
        productionContext.Set(StackingModuleConstants.LastCloudUploadAtKey, DateTime.Now);

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            productionContext.RemoveDeviceData(StackingModuleConstants.LastCloudUploadErrorKey);
            return;
        }

        productionContext.Set(StackingModuleConstants.LastCloudUploadErrorKey, errorMessage);
    }
}
