using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Modules.Cloud;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Module.Stacking.Integration;

/// <summary>
/// 叠片 Cloud 上传器。上传框架由 Application 提供，本类只保留叠片 payload 映射和诊断状态写入。
/// </summary>
public sealed class StackingCloudUploader : CloudUploadChannelBase<StackingCellData, object>
{
    /// <summary>
    /// 叠片云端单条过站接口路径。
    /// </summary>
    private const string UploadPathValue = "/api/v1/edge/pass-stations/stacking";

    private readonly IConfiguration _configuration;
    private readonly IProductionContextStore _contextStore;
    private readonly IProductionTimeProvider _productionTime;

    public StackingCloudUploader(
        ICloudHttpClient cloudHttp,
        ILogService logger,
        IConfiguration configuration,
        IProductionContextStore contextStore,
        IProductionTimeProvider productionTime)
        : base(StackingModuleConstants.ProcessType, ProcessUploadMode.Single, UploadPathValue, cloudHttp, logger)
    {
        _configuration = configuration;
        _contextStore = contextStore;
        _productionTime = productionTime;
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
        // 叠片当前按单条上传，云端只接收当前记录 item。
        => new
        {
            deviceId = context.Device.DeviceId,
            item = ToCloudDto(cellData[0])
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
        productionContext.Set(StackingModuleConstants.LastCloudUploadAtKey, _productionTime.BusinessNow);

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            productionContext.RemoveDeviceData(StackingModuleConstants.LastCloudUploadErrorKey);
            return;
        }

        productionContext.Set(StackingModuleConstants.LastCloudUploadErrorKey, errorMessage);
    }

    private StackingCloudDto ToCloudDto(StackingCellData source)
        => new()
        {
            Barcode = source.Barcode,
            TrayCode = source.TrayCode,
            LayerCount = source.LayerCount,
            SequenceNo = source.SequenceNo,
            CellResult = source.CellResult == true
                ? "OK"
                : source.CellResult == false
                    ? "NG"
                    : "Unknown",
            CompletedTime = _productionTime.ToBusinessTime(source.CompletedTime ?? _productionTime.UtcNow)
        };
}
