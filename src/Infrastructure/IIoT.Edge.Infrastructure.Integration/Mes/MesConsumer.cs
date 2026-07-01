using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration;
using IIoT.Edge.SharedKernel.DataPipeline;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Infrastructure.Integration.Mes;

public sealed class MesConsumer : ProcessUploaderConsumerBase<IProcessMesUploader, MesCallResult>, IMesConsumer
{
    private readonly IMesUploadGate _uploadGate;
    private readonly IProcessIntegrationRegistry _processIntegrationRegistry;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;

    public string Name => "MES";
    public int Order => 20;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;
    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.Mes;

    public MesConsumer(
        IDeviceService deviceService,
        IMesUploadGate uploadGate,
        IEnumerable<IProcessMesUploader> uploaders,
        IProcessIntegrationRegistry processIntegrationRegistry,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ILogService logger)
        : base(deviceService, uploaders, logger)
    {
        _uploadGate = uploadGate;
        _processIntegrationRegistry = processIntegrationRegistry;
        _diagnosticsStore = diagnosticsStore;
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
    {
        var processType = record.CellData.ProcessType;
        var isRegistered = _processIntegrationRegistry.HasMesUploader(processType);
        if (!isRegistered)
        {
            return true;
        }

        var gate = _uploadGate.GetSnapshot();
        if (!gate.CanUpload && string.Equals(gate.ReasonCode, "mes_upload_disabled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!gate.CanUpload)
        {
            var reason = string.IsNullOrWhiteSpace(gate.ReasonCode)
                ? "mes_upload_gate_blocked"
                : gate.ReasonCode;
            _diagnosticsStore.RecordFailure(processType, reason);
            Logger.Warn($"[MES] 上传门控未就绪（{reason}），本次数据转入 MES 补偿队列。工序={processType}");
            return false;
        }

        var device = CurrentDevice;
        if (device is null)
        {
            const string reason = "尚未识别当前设备。";
            _diagnosticsStore.RecordFailure(processType, reason);
            Logger.Warn($"[MES] {reason} 工序={processType}");
            return false;
        }

        if (!TryResolveUploader(
                "MES",
                processType,
                isRegistered,
                out var uploader,
                out _))
        {
            const string reason = "uploader_not_found";
            _diagnosticsStore.RecordFailure(processType, reason);
            return false;
        }

        var result = await uploader
            .UploadAsync(new ProcessUploadContext(device), [record], cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _diagnosticsStore.RecordSuccess(processType);
            return true;
        }

        _diagnosticsStore.RecordFailure(processType, result.Message);
        Logger.Error(
            $"[MES] Upload failed for process type {processType}. Outcome:{result.Outcome}, Message:{result.Message}");
        return false;
    }
}
