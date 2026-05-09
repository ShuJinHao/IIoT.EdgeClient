using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Device;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Infrastructure.Integration.PassStation;

public class CloudConsumer : ICloudConsumer, ICloudBatchConsumer
{
    private readonly IDeviceService _deviceService;
    private readonly ICloudUploadGate _uploadGate;
    private readonly ILogService _logger;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
    private readonly Dictionary<string, IProcessCloudUploader> _uploaders;

    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.Cloud;
    public string Name => "Cloud";
    public int Order => 25;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;

    public CloudConsumer(
        IDeviceService deviceService,
        ICloudUploadGate uploadGate,
        IEnumerable<IProcessCloudUploader> uploaders,
        ICloudUploadDiagnosticsStore diagnosticsStore,
        ILogService logger)
    {
        _deviceService = deviceService;
        _uploadGate = uploadGate;
        _diagnosticsStore = diagnosticsStore;
        _logger = logger;
        _uploaders = uploaders.ToDictionary(x => x.ProcessType, StringComparer.OrdinalIgnoreCase);
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

        var gate = _uploadGate.GetSnapshot();
        if (!gate.CanUpload && string.Equals(gate.ReasonCode, "cloud_upload_disabled", StringComparison.OrdinalIgnoreCase))
        {
            var skippedResult = CloudCallResult.Success();
            _diagnosticsStore.RecordResult(records[0].CellData.ProcessType, skippedResult);
            return skippedResult;
        }

        if (!gate.CanUpload)
        {
            var blockedResult = CloudCallResult.Failure(
                CloudCallOutcome.SkippedUploadNotReady,
                gate.ReasonCode);
            _logger.Warn(
                $"[Cloud] 上传门控已阻塞（{gate.ReasonCode}），{records.Count} 条记录转入 retry 队列。");
            _diagnosticsStore.RecordResult(records[0].CellData.ProcessType, blockedResult);
            return blockedResult;
        }

        var device = _deviceService.CurrentDevice;
        if (device is null)
        {
            var unidentifiedResult = CloudCallResult.Failure(
                CloudCallOutcome.SkippedUploadNotReady,
                EdgeUploadBlockReason.DeviceUnidentified.ToReasonCode());
            _logger.Warn("[Cloud] 设备尚未识别，记录转入 retry 队列。");
            _diagnosticsStore.RecordResult(records[0].CellData.ProcessType, unidentifiedResult);
            return unidentifiedResult;
        }

        var context = new ProcessCloudUploadContext(device);
        foreach (var group in records.GroupBy(x => x.CellData.ProcessType, StringComparer.OrdinalIgnoreCase))
        {
            if (!_uploaders.TryGetValue(group.Key, out var uploader))
            {
                var uploaderMissing = CloudCallResult.Failure(CloudCallOutcome.Exception, "uploader_not_found");
                _logger.Error($"[Cloud] 工序 {group.Key} 未注册云端上传器。");
                _diagnosticsStore.RecordResult(group.Key, uploaderMissing);
                return uploaderMissing;
            }

            var result = await uploader.UploadAsync(context, group.ToList(), cancellationToken).ConfigureAwait(false);
            _diagnosticsStore.RecordResult(group.Key, result);
            if (!result.IsSuccess)
            {
                _logger.Error(
                    $"[Cloud] 工序 {group.Key} 上传失败，数量：{group.Count()}，结果：{result.Outcome}，原因：{result.ReasonCode}。");
                return result;
            }
        }

        return CloudCallResult.Success();
    }
}
