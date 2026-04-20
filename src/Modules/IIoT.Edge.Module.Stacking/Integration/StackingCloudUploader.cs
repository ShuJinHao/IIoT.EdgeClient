using AutoMapper;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Http;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.Stacking.Integration;

public sealed class StackingCloudUploader : IProcessCloudUploader
{
    private const string UploadPath = "/api/v1/edge/pass-stations/stacking";

    private readonly ICloudHttpClient _cloudHttp;
    private readonly IMapper _mapper;
    private readonly ILogService _logger;
    private readonly IConfiguration _configuration;
    private readonly IProductionContextStore _contextStore;

    public StackingCloudUploader(
        ICloudHttpClient cloudHttp,
        IMapper mapper,
        ILogService logger,
        IConfiguration configuration,
        IProductionContextStore contextStore)
    {
        _cloudHttp = cloudHttp;
        _mapper = mapper;
        _logger = logger;
        _configuration = configuration;
        _contextStore = contextStore;
    }

    public string ProcessType => StackingModuleConstants.ProcessType;

    public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

    public async Task<CloudCallResult> UploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return CloudCallResult.Success();
        }

        var isEnabled = _configuration.GetValue<bool>("Modules:Stacking:CloudUploadEnabled");
        if (!isEnabled)
        {
            var deviceName = ResolveDeviceName(records[0], context);
            const string errorMessage = "Stacking cloud upload is disabled by configuration.";
            UpdateDiagnostics(deviceName, false, StackingModuleConstants.CloudUploadDisabledStatus, errorMessage);
            _logger.Warn($"[Cloud] {errorMessage}");
            return CloudCallResult.Failure(CloudCallOutcome.Exception, "cloud_upload_disabled");
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
                return CloudCallResult.Failure(CloudCallOutcome.Exception, "unexpected_process_type");
            }

            var payload = new
            {
                deviceId = context.Device.DeviceId,
                item = _mapper.Map<StackingCloudDto>(stacking)
            };

            var result = await _cloudHttp.PostAsync(
                UploadPath,
                payload,
                new CloudRequestOptions
                {
                    IdempotencyKey = CloudIdempotencyKeyBuilder.ForRecord(
                        ProcessType,
                        nameof(StackingCloudUploader),
                        record)
                }).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                var errorMessage =
                    $"Cloud API returned failure for Stacking barcode '{stacking.Barcode}'. Outcome:{result.Outcome}, Reason:{result.ReasonCode}.";
                UpdateDiagnostics(
                    ResolveDeviceName(record, context),
                    true,
                    StackingModuleConstants.CloudUploadFailedStatus,
                    errorMessage);
                _logger.Error($"[Cloud] {errorMessage}");
                return result;
            }

            UpdateDiagnostics(
                ResolveDeviceName(record, context),
                true,
                StackingModuleConstants.CloudUploadSuccessStatus,
                errorMessage: null);
        }

        return CloudCallResult.Success();
    }

    private static string ResolveDeviceName(CellCompletedRecord record, ProcessCloudUploadContext context)
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
        productionContext.Set(StackingModuleConstants.LastCloudUploadAtKey, DateTime.UtcNow);

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            productionContext.RemoveDeviceData(StackingModuleConstants.LastCloudUploadErrorKey);
            return;
        }

        productionContext.Set(StackingModuleConstants.LastCloudUploadErrorKey, errorMessage);
    }
}
