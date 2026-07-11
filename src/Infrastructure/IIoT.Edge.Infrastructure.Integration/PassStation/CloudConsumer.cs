using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Device;
using IIoT.Edge.Infrastructure.Integration;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

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
        => (await ProcessWithResultAsync(record, cancellationToken).ConfigureAwait(false)).IsSuccess;

    public Task<CloudCallResult> ProcessWithResultAsync(
        CellCompletedRecord record,
        CancellationToken cancellationToken = default)
        => ProcessBatchAsync([record], cancellationToken);

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
                return disabledResult;
            }

            if (!_processIntegrationRegistry.TryGetCloudUploader(group.Key, out var registration))
            {
                const string reasonCode = "cloud_uploader_missing";
                var missingUploaderResult = CloudCallResult.Failure(
                    CloudCallOutcome.SkippedUploadNotReady,
                    reasonCode);
                _logger.Warn(
                    $"[PLC-{UploadDiagnosticsContextFactory.ResolveLogDeviceName(group)}][云端] 工序 {group.Key} 未注册 Cloud 上传器，{group.Count()} 条记录转入补传队列。");
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
                var blockedDevice = UploadDiagnosticsContextFactory.ResolveLogDeviceName(group);
                var blockedResult = CloudCallResult.Failure(
                    CloudCallOutcome.SkippedUploadNotReady,
                    gate.ReasonCode);
                _logger.Warn(
                    $"[PLC-{blockedDevice}][云端] 上传门控已阻塞（{gate.ReasonCode}），{group.Count()} 条记录转入补传队列。");
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
                var deviceStatusRecords = groupRecords
                    .Where(UploadDiagnosticsContextFactory.IsDeviceStatusRecord)
                    .ToList();
                if (deviceStatusRecords.Count > 0)
                {
                    const string reasonCode = "cloud_plc_device_status_endpoint_missing";
                    _logger.Warn(
                        $"[PLC-{UploadDiagnosticsContextFactory.ResolveLogDeviceName(deviceStatusRecords)}][云端] PLC 设备状态专用接口未就绪，{deviceStatusRecords.Count} 条设备状态记录已跳过标准过站上传。");
                    _diagnosticsStore.RecordBlocked(
                        group.Key,
                        reasonCode,
                        "PLC 设备状态 Cloud 专用接口未就绪，已跳过标准过站上传。",
                        UploadDiagnosticsContextFactory.CreateCloudContext(deviceStatusRecords));
                    groupRecords = groupRecords
                        .Where(static record => !UploadDiagnosticsContextFactory.IsDeviceStatusRecord(record))
                        .ToList();
                    if (groupRecords.Count == 0)
                    {
                        continue;
                    }
                }

                var device = ResolveUploadDevice(groupRecords, _deviceService.CurrentDevice);
                if (device is null)
                {
                    var unidentifiedDevice = UploadDiagnosticsContextFactory.ResolveLogDeviceName(groupRecords);
                    var unidentifiedResult = CloudCallResult.Failure(
                        CloudCallOutcome.SkippedUploadNotReady,
                        EdgeUploadBlockReason.DeviceUnidentified.ToReasonCode());
                    _logger.Warn($"[PLC-{unidentifiedDevice}][云端] 设备尚未识别，记录转入补传队列。");
                    _diagnosticsStore.RecordBlocked(
                        group.Key,
                        EdgeUploadBlockReason.DeviceUnidentified.ToReasonCode(),
                        "设备尚未识别。",
                        UploadDiagnosticsContextFactory.CreateCloudContext(groupRecords));
                    return unidentifiedResult;
                }

                var context = new ProcessUploadContext(device);
                var result = registration.UploadMode == ProcessUploadMode.Batch
                    ? await _standardUploader.UploadAsync(context, group.Key, groupRecords, cancellationToken).ConfigureAwait(false)
                    : await UploadOneByOneAsync(context, group.Key, groupRecords, cancellationToken).ConfigureAwait(false);
                _diagnosticsStore.RecordResult(group.Key, result, UploadDiagnosticsContextFactory.CreateCloudContext(groupRecords));
                if (!result.IsSuccess)
                {
                    _logger.Error(
                        $"[PLC-{UploadDiagnosticsContextFactory.ResolveLogDeviceName(groupRecords)}][云端] 工序 {group.Key} 上传失败，数量：{groupRecords.Count}，结果：{result.Outcome}，原因：{result.ReasonCode}。");
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

            if (!result.IsSuccess)
            {
                return result;
            }
        }

        return CloudCallResult.Success();
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
