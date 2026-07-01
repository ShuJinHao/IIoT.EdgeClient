using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Device;
using IIoT.Edge.SharedKernel.DataPipeline;

using IIoT.Edge.Application.Abstractions.Cloud;
namespace IIoT.Edge.Infrastructure.Integration.PassStation;

public class CloudConsumer : ICloudConsumer, ICloudBatchConsumer
{
    private readonly IDeviceService _deviceService;
    private readonly ICloudUploadGate _uploadGate;
    private readonly StandardPassStationCloudUploader _standardUploader;
    private readonly IProcessIntegrationRegistry _processIntegrationRegistry;
    private readonly IModuleParamRoleProvider _moduleParamRoleProvider;
    private readonly ICloudUploadDiagnosticsStore _diagnosticsStore;
    private readonly ILogService _logger;

    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.Cloud;
    public string Name => "Cloud";
    public int Order => 25;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;

    public CloudConsumer(
        IDeviceService deviceService,
        ICloudUploadGate uploadGate,
        StandardPassStationCloudUploader standardUploader,
        IProcessIntegrationRegistry processIntegrationRegistry,
        IModuleParamRoleProvider moduleParamRoleProvider,
        ICloudUploadDiagnosticsStore diagnosticsStore,
        ILogService logger)
    {
        _deviceService = deviceService;
        _uploadGate = uploadGate;
        _standardUploader = standardUploader;
        _processIntegrationRegistry = processIntegrationRegistry;
        _moduleParamRoleProvider = moduleParamRoleProvider;
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

        foreach (var group in records.GroupBy(x => x.CellData.ProcessType, StringComparer.OrdinalIgnoreCase))
        {
            if (!_processIntegrationRegistry.TryGetCloudUploader(group.Key, out var registration))
            {
                continue;
            }

            if (!await IsPluginCloudEnabledAsync(group.Key, cancellationToken).ConfigureAwait(false))
            {
                var skippedResult = CloudCallResult.Success();
                _diagnosticsStore.RecordResult(group.Key, skippedResult);
                continue;
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
                _logger.Warn(
                    $"[云端] 上传门控已阻塞（{gate.ReasonCode}），{group.Count()} 条记录转入补传队列。");
                _diagnosticsStore.RecordResult(group.Key, blockedResult);
                return blockedResult;
            }

            var device = _deviceService.CurrentDevice;
            if (device is null)
            {
                var unidentifiedResult = CloudCallResult.Failure(
                    CloudCallOutcome.SkippedUploadNotReady,
                    EdgeUploadBlockReason.DeviceUnidentified.ToReasonCode());
                _logger.Warn("[云端] 设备尚未识别，记录转入补传队列。");
                _diagnosticsStore.RecordResult(group.Key, unidentifiedResult);
                return unidentifiedResult;
            }

            var groupRecords = group.ToList();
            var context = new ProcessUploadContext(device);
            var result = registration.UploadMode == ProcessUploadMode.Batch
                ? await _standardUploader.UploadAsync(context, group.Key, groupRecords, cancellationToken).ConfigureAwait(false)
                : await UploadOneByOneAsync(context, group.Key, groupRecords, cancellationToken).ConfigureAwait(false);
            _diagnosticsStore.RecordResult(group.Key, result);
            if (!result.IsSuccess)
            {
                _logger.Error(
                    $"[云端] 工序 {group.Key} 上传失败，数量：{groupRecords.Count}，结果：{result.Outcome}，原因：{result.ReasonCode}。");
                return result;
            }
        }

        return CloudCallResult.Success();
    }

    private Task<bool> IsPluginCloudEnabledAsync(string processType, CancellationToken cancellationToken)
        => _moduleParamRoleProvider.GetBoolAsync(
            processType,
            ModuleParamCategory.Cloud,
            ModuleParamRole.CloudEnabled,
            defaultValue: true,
            cancellationToken);

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
}
