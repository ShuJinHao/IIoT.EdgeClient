using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.Consumers;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Common.Device;
using IIoT.Edge.Infrastructure.Integration;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

namespace IIoT.Edge.Infrastructure.Integration.PassStation;

public class CloudConsumer : ICloudConsumer, ICloudBatchConsumer
{
    private readonly IDeviceService _deviceService;
    private readonly ICloudExecutionPolicy _executionPolicy;
    private readonly ICloudUploadGate _uploadGate;
    private readonly StandardPassStationCloudUploader _standardUploader;
    private readonly IProcessIntegrationRegistry _processIntegrationRegistry;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
    private readonly ILogService _logger;

    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.Cloud;
    public string Name => "Cloud";
    public int Order => 25;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;

    public CloudConsumer(
        IDeviceService deviceService,
        ICloudExecutionPolicy executionPolicy,
        ICloudUploadGate uploadGate,
        StandardPassStationCloudUploader standardUploader,
        IProcessIntegrationRegistry processIntegrationRegistry,
        ICloudUploadDiagnosticsStore diagnosticsStore,
        ILogService logger)
    {
        _deviceService = deviceService;
        _executionPolicy = executionPolicy;
        _uploadGate = uploadGate;
        _standardUploader = standardUploader;
        _processIntegrationRegistry = processIntegrationRegistry;
        _diagnosticsStore = diagnosticsStore;
        _logger = logger;
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
    {
        var result = await ProcessWithResultAsync(record, cancellationToken).ConfigureAwait(false);
        if (result.Outcome == CloudCallOutcome.InvalidPayload)
        {
            throw new DataPipelineNonRetryableException(result.ReasonCode);
        }

        return result.IsSuccess;
    }

    public Task<CloudCallResult> ProcessWithResultAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default)
        => ProcessBatchAsync([record], cancellationToken);

    public CloudCallResult ValidateBatchRecord(CellCompletedRecord record)
        => StandardPassStationCloudUploader.ValidateRecord(record.CellData.ProcessType, record);

    public async Task<CloudCallResult> ProcessBatchAsync(
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return CloudCallResult.Success();
        }

        var cloudRecords = records
            .Where(x => x.CellData.UploadTargets.HasFlag(DataPipelineUploadTargets.Cloud))
            .ToList();
        if (cloudRecords.Count == 0)
        {
            return CloudCallResult.Success();
        }

        var deviceStatusRecords = cloudRecords
            .Where(UploadDiagnosticsContextFactory.IsDeviceStatusRecord)
            .ToList();
        if (deviceStatusRecords.Count > 0)
        {
            const string reasonCode = "cloud_plc_device_status_endpoint_missing";
            var blockedResult = CloudCallResult.Failure(
                CloudCallOutcome.SkippedUploadNotReady,
                reasonCode);
            LogEach(
                deviceStatusRecords,
                "Blocked",
                reasonCode,
                "PLC 设备状态专用接口未就绪，记录保持待传。",
                isError: false);
            _diagnosticsStore.RecordBlocked(
                deviceStatusRecords[0].CellData.ProcessType,
                reasonCode,
                "PLC 设备状态 Cloud 专用接口未就绪，记录保持待传。",
                UploadDiagnosticsContextFactory.CreateCloudContext(deviceStatusRecords));
            return blockedResult;
        }

        foreach (var group in cloudRecords.GroupBy(x => x.CellData.ProcessType, StringComparer.OrdinalIgnoreCase))
        {
            if (!_executionPolicy.IsEnabled)
            {
                const string reasonCode = "cloud_upload_disabled";
                var disabledResult = CloudCallResult.Failure(
                    CloudCallOutcome.SkippedUploadNotReady,
                    reasonCode);
                _diagnosticsStore.RecordBlocked(
                    group.Key,
                    reasonCode,
                    "当前 profile 的 Cloud 通信已关闭。",
                    UploadDiagnosticsContextFactory.CreateCloudContext(group));
                LogEach(
                    group,
                    "Blocked",
                    reasonCode,
                    "Cloud 数据面已关闭，记录保持待传。",
                    isError: false);
                return disabledResult;
            }

            if (!_processIntegrationRegistry.TryGetCloudUploader(group.Key, out var registration))
            {
                const string reasonCode = "cloud_uploader_missing";
                var missingUploaderResult = CloudCallResult.Failure(
                    CloudCallOutcome.SkippedUploadNotReady,
                    reasonCode);
                LogEach(
                    group,
                    "Blocked",
                    reasonCode,
                    "未注册 Cloud 上传器，将交接到 Cloud 持久补偿链。",
                    isError: false);
                _diagnosticsStore.RecordBlocked(
                    group.Key,
                    reasonCode,
                    "工序未注册 Cloud 上传器。",
                    UploadDiagnosticsContextFactory.CreateCloudContext(group));
                return missingUploaderResult;
            }

            var gate = _uploadGate.GetSnapshot();
            if (!gate.CanUpload)
            {
                var blockedResult = CloudCallResult.Failure(
                    CloudCallOutcome.SkippedUploadNotReady,
                    gate.ReasonCode);
                LogEach(
                    group,
                    "Blocked",
                    "cloud_upload_gate_blocked",
                    "Cloud 上传门控未就绪，将交接到 Cloud 持久补偿链。",
                    isError: false);
                _diagnosticsStore.RecordBlocked(
                    group.Key,
                    gate.ReasonCode,
                    gate.Message,
                    UploadDiagnosticsContextFactory.CreateCloudContext(group));
                return blockedResult;
            }

            foreach (var sourceGroup in group.GroupBy(UploadDiagnosticsContextFactory.CreateSourceKey))
            {
                var groupRecords = sourceGroup.ToList();
                var device = ResolveUploadDevice(groupRecords, _deviceService.CurrentDevice);
                if (device is null)
                {
                    var unidentifiedResult = CloudCallResult.Failure(
                        CloudCallOutcome.SkippedUploadNotReady,
                        EdgeUploadBlockReason.DeviceUnidentified.ToReasonCode());
                    LogEach(
                        groupRecords,
                        "Blocked",
                        EdgeUploadBlockReason.DeviceUnidentified.ToReasonCode(),
                        "Cloud 设备身份尚未识别，将交接到 Cloud 持久补偿链。",
                        isError: false);
                    _diagnosticsStore.RecordBlocked(
                        group.Key,
                        EdgeUploadBlockReason.DeviceUnidentified.ToReasonCode(),
                        "设备尚未识别。",
                        UploadDiagnosticsContextFactory.CreateCloudContext(groupRecords));
                    return unidentifiedResult;
                }

                var context = new ProcessUploadContext(device);
                CloudCallResult result;
                if (registration.UploadMode == ProcessUploadMode.Batch)
                {
                    result = await _standardUploader
                        .UploadAsync(context, group.Key, groupRecords, cancellationToken)
                        .ConfigureAwait(false);
                    LogCloudResult(groupRecords, result);
                }
                else
                {
                    result = await UploadOneByOneAsync(
                            context,
                            group.Key,
                            groupRecords,
                            cancellationToken)
                        .ConfigureAwait(false);
                }

                _diagnosticsStore.RecordResult(group.Key, result, UploadDiagnosticsContextFactory.CreateCloudContext(groupRecords));
                if (!result.IsSuccess)
                {
                    return result;
                }
            }
        }

        return CloudCallResult.Success();
    }

    private async Task<CloudCallResult> UploadOneByOneAsync(
        ProcessUploadContext context,
        string processType,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            var result = await _standardUploader.UploadAsync(
                    context,
                    processType,
                    [record],
                    cancellationToken)
                .ConfigureAwait(false);

            LogCloudResult([record], result);

            if (!result.IsSuccess)
            {
                return result;
            }
        }

        return CloudCallResult.Success();
    }

    private void LogCloudResult(
        IReadOnlyList<CellCompletedRecord> records,
        CloudCallResult result)
    {
        if (result.IsSuccess)
        {
            LogEach(
                records,
                "Uploaded",
                reasonCode: null,
                "Cloud 接口已返回正式成功结果。",
                isError: false);
            return;
        }

        LogEach(
            records,
            "Failed",
            UploadTraceLogFormatter.ReasonCode("cloud_upload", result.Outcome),
            "Cloud 接收未成功，将交接到 Cloud 持久补偿链。",
            isError: true);
    }

    private void LogEach(
        IEnumerable<CellCompletedRecord> records,
        string result,
        string? reasonCode,
        string message,
        bool isError)
    {
        foreach (var record in records)
        {
            var entry =
                $"{UploadTraceLogFormatter.Format(record, "Cloud")}[云端直传] " +
                $"结果={result}" +
                (string.IsNullOrWhiteSpace(reasonCode) ? string.Empty : $"，原因码={reasonCode}") +
                $"；{message}";
            if (isError)
            {
                _logger.Error(entry);
            }
            else if (string.Equals(result, "Uploaded", StringComparison.Ordinal))
            {
                _logger.Info(entry);
            }
            else
            {
                _logger.Warn(entry);
            }
        }
    }

    private static DeviceSession? ResolveUploadDevice(
        IReadOnlyList<CellCompletedRecord> records,
        DeviceSession? currentDevice)
    {
        if (currentDevice is null)
        {
            return null;
        }

        var deviceNames = records
            .Select(record => record.ResolveDeviceName())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return deviceNames.Count == 1
            ? currentDevice with { DeviceName = deviceNames[0] }
            : currentDevice;
    }

}
