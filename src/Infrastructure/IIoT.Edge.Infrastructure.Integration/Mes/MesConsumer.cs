using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Infrastructure.Integration;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

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
        IMesUploadGate uploadGate,
        IEnumerable<IProcessMesUploader> uploaders,
        IProcessIntegrationRegistry processIntegrationRegistry,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ILogService logger)
        : base(uploaders, logger)
    {
        _uploadGate = uploadGate;
        _processIntegrationRegistry = processIntegrationRegistry;
        _diagnosticsStore = diagnosticsStore;
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
    {
        if (!record.CellData.UploadTargets.HasFlag(DataPipelineUploadTargets.Mes))
        {
            return true;
        }

        var processType = record.CellData.ProcessType;
        var logContext = UploadTraceLogFormatter.Format(record, "MES");
        var isRegistered = _processIntegrationRegistry.HasMesUploader(processType);
        if (!isRegistered)
        {
            const string reason = "uploader_not_registered";
            _diagnosticsStore.RecordFailure(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Error(
                $"{logContext}[MES直传] 结果=Failed，原因码={reason}。");
            return false;
        }

        var gate = _uploadGate.GetSnapshot();
        if (!gate.CanUpload)
        {
            var reason = string.IsNullOrWhiteSpace(gate.ReasonCode)
                ? "mes_upload_gate_blocked"
                : gate.ReasonCode;
            _diagnosticsStore.RecordBlocked(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Warn(
                $"{logContext}[MES直传] 结果=Blocked，原因码=mes_upload_gate_blocked；" +
                "将交接到 MES 持久补偿链。");
            return false;
        }

        if (!TryResolveUploader(
                processType,
                isRegistered,
                out var uploader,
                out _))
        {
            const string reason = "uploader_not_found";
            _diagnosticsStore.RecordFailure(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Error(
                $"{logContext}[MES直传] 结果=Failed，原因码={reason}。");
            return false;
        }

        // MES 身份只由插件 MES 参数提供；公共上传上下文仅携带记录自己的 PLC 展示名称，
        // 不得继承 Cloud bootstrap 得到的 DeviceId 或 ClientCode。
        var uploadContext = new ProcessUploadContext(new DeviceSession
        {
            DeviceName = record.ResolveDeviceName()
        });
        var result = await uploader
            .UploadAsync(uploadContext, [record], cancellationToken)
            .ConfigureAwait(false);
        if (result.Outcome == MesCallOutcome.Success)
        {
            _diagnosticsStore.RecordSuccess(processType, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Info($"{logContext}[MES直传] 结果=Uploaded。");
            return true;
        }

        if (result.Outcome == MesCallOutcome.Disabled)
        {
            const string reason = "mes_uploader_disabled";
            _diagnosticsStore.RecordBlocked(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Warn(
                $"{logContext}[MES直传] 结果=Blocked，原因码={reason}；" +
                "将交接到 MES 持久补偿链。");
            return false;
        }

        var failureReason = UploadTraceLogFormatter.ReasonCode("mes_upload", result.Outcome);
        _diagnosticsStore.RecordFailure(processType, failureReason, UploadDiagnosticsContextFactory.CreateMesContext(record));
        Logger.Error(
            $"{logContext}[MES直传] 结果=Failed，原因码={failureReason}；" +
            "将交接到 MES 持久补偿链。");
        return false;
    }
}
