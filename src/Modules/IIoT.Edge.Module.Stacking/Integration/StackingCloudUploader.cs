using AutoMapper;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.Stacking.Integration;

public sealed class StackingCloudUploader : ProcessCloudUploaderBase<StackingCellData, object>
{
    private const string UploadPathValue = "/api/v1/edge/pass-stations/stacking";

    private readonly IMapper _mapper;
    private readonly IConfiguration _configuration;
    private readonly IProductionContextStore _contextStore;

    public StackingCloudUploader(
        ICloudHttpClient cloudHttp,
        IMapper mapper,
        ILogService logger,
        IConfiguration configuration,
        IProductionContextStore contextStore)
        : base(StackingModuleConstants.ProcessType, ProcessUploadMode.Single, UploadPathValue, cloudHttp, logger)
    {
        _mapper = mapper;
        _configuration = configuration;
        _contextStore = contextStore;
    }

    protected override Task<CloudCallResult?> CheckBeforeUploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken)
    {
        var isEnabled = _configuration.GetValue<bool>("Modules:Stacking:CloudUploadEnabled");
        if (isEnabled)
        {
            return Task.FromResult<CloudCallResult?>(null);
        }

        var deviceName = ResolveDeviceName(records[0], context);
        const string message = "叠片云端上传已被配置关闭。";
        UpdateDiagnostics(deviceName, false, StackingModuleConstants.CloudUploadDisabledStatus, message);
        Logger.Warn($"[Cloud] {message}");
        return Task.FromResult<CloudCallResult?>(
            CloudCallResult.Failure(CloudCallOutcome.Exception, "cloud_upload_disabled"));
    }

    protected override object BuildPayload(
        ProcessCloudUploadContext context,
        IReadOnlyList<StackingCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records)
        => new
        {
            deviceId = context.Device.DeviceId,
            item = _mapper.Map<StackingCloudDto>(cellData[0])
        };

    protected override Task OnUploadSucceededAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<StackingCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken)
    {
        UpdateDiagnostics(
            ResolveDeviceName(records[0], context),
            true,
            StackingModuleConstants.CloudUploadSuccessStatus,
            errorMessage: null);
        return Task.CompletedTask;
    }

    protected override Task OnUploadFailedAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CloudCallResult result,
        string message,
        CancellationToken cancellationToken)
    {
        UpdateDiagnostics(
            ResolveDeviceName(records[0], context),
            true,
            StackingModuleConstants.CloudUploadFailedStatus,
            message);
        return Task.CompletedTask;
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
