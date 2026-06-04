using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Device;
using IIoT.Edge.Infrastructure.Integration;
using IIoT.Edge.SharedKernel.DataPipeline;

using IIoT.Edge.Application.Abstractions.Cloud;
namespace IIoT.Edge.Infrastructure.Integration.PassStation;

public class CloudConsumer : ProcessUploaderConsumerBase<IProcessCloudUploader, CloudCallResult>, ICloudConsumer, ICloudBatchConsumer
{
    private readonly ICloudUploadGate _uploadGate;
    private readonly IProcessIntegrationRegistry _processIntegrationRegistry;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;

    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.Cloud;
    public string Name => "Cloud";
    public int Order => 25;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;

    public CloudConsumer(
        IDeviceService deviceService,
        ICloudUploadGate uploadGate,
        IEnumerable<IProcessCloudUploader> uploaders,
        IProcessIntegrationRegistry processIntegrationRegistry,
        ICloudUploadDiagnosticsStore diagnosticsStore,
        ILogService logger)
        : base(deviceService, uploaders, logger)
    {
        _uploadGate = uploadGate;
        _processIntegrationRegistry = processIntegrationRegistry;
        _diagnosticsStore = diagnosticsStore;
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

        foreach (var group in records.GroupBy(x => x.CellData.ProcessType, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryResolveUploader(
                    "Cloud",
                    group.Key,
                    _processIntegrationRegistry.HasCloudUploader(group.Key),
                    out var uploader,
                    out var shouldFail))
            {
                if (!shouldFail)
                {
                    continue;
                }

                var uploaderMissing = CloudCallResult.Failure(CloudCallOutcome.Exception, "uploader_not_found");
                _diagnosticsStore.RecordResult(group.Key, uploaderMissing);
                return uploaderMissing;
            }

            var gate = _uploadGate.GetSnapshot();
            if (!gate.CanUpload && string.Equals(gate.ReasonCode, "cloud_upload_disabled", StringComparison.OrdinalIgnoreCase))
            {
                var skippedResult = CloudCallResult.Success();
                _diagnosticsStore.RecordResult(group.Key, skippedResult);
                continue;
            }

            if (!gate.CanUpload)
            {
                var blockedResult = CloudCallResult.Failure(
                    CloudCallOutcome.SkippedUploadNotReady,
                    gate.ReasonCode);
                Logger.Warn(
                    $"[Cloud] 上传门控已阻塞（{gate.ReasonCode}），{group.Count()} 条记录转入 retry 队列。");
                _diagnosticsStore.RecordResult(group.Key, blockedResult);
                return blockedResult;
            }

            var device = CurrentDevice;
            if (device is null)
            {
                var unidentifiedResult = CloudCallResult.Failure(
                    CloudCallOutcome.SkippedUploadNotReady,
                    EdgeUploadBlockReason.DeviceUnidentified.ToReasonCode());
                Logger.Warn("[Cloud] 设备尚未识别，记录转入 retry 队列。");
                _diagnosticsStore.RecordResult(group.Key, unidentifiedResult);
                return unidentifiedResult;
            }

            var groupRecords = group.ToList();
            var context = new ProcessUploadContext(device);
            var result = await uploader.UploadAsync(context, groupRecords, cancellationToken).ConfigureAwait(false);
            _diagnosticsStore.RecordResult(group.Key, result);
            if (!result.IsSuccess)
            {
                Logger.Error(
                    $"[Cloud] 工序 {group.Key} 上传失败，数量：{groupRecords.Count}，结果：{result.Outcome}，原因：{result.ReasonCode}。");
                return result;
            }
        }

        return CloudCallResult.Success();
    }
}
