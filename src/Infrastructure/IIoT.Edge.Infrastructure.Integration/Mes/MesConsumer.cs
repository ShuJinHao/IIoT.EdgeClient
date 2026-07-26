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
        var isRegistered = _processIntegrationRegistry.HasMesUploader(processType);
        if (!isRegistered)
        {
            const string reason = "uploader_not_registered";
            _diagnosticsStore.RecordFailure(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Error(
                $"[PLC-{record.ResolveDeviceName()}][MES] 工序 {processType} 的记录已指定 MES 目标，但未注册上传器。");
            return false;
        }

        var gate = _uploadGate.GetSnapshot();
        if (!gate.CanUpload)
        {
            var blockedDeviceName = record.ResolveDeviceName();
            var reason = string.IsNullOrWhiteSpace(gate.ReasonCode)
                ? "mes_upload_gate_blocked"
                : gate.ReasonCode;
            _diagnosticsStore.RecordBlocked(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Warn($"[PLC-{blockedDeviceName}][MES] 上传门控未就绪（{reason}），本次数据转入 MES 补偿队列。工序={processType}");
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
            _diagnosticsStore.RecordFailure(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Error($"[PLC-{record.ResolveDeviceName()}][MES] 工序 {processType} 已注册上传器，但未找到可用实现。");
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
        var deviceName = uploadContext.Device.DeviceName;

        if (result.Outcome == MesCallOutcome.Success)
        {
            _diagnosticsStore.RecordSuccess(processType, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Info($"[PLC-{deviceName}][MES] 工序 {processType} 上传成功。");
            return true;
        }

        if (result.Outcome == MesCallOutcome.Disabled)
        {
            const string reason = "mes_uploader_disabled";
            _diagnosticsStore.RecordBlocked(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Warn(
                $"[PLC-{deviceName}][MES] 工序 {processType} 上传器已禁用（{reason}），本次数据转入 MES 补偿队列。原因：{result.Message}");
            return false;
        }

        _diagnosticsStore.RecordFailure(processType, result.Message, UploadDiagnosticsContextFactory.CreateMesContext(record));
        Logger.Error(
            $"[PLC-{deviceName}][MES] 工序 {processType} 上传失败，结果：{result.Outcome}，原因：{result.Message}");
        return false;
    }
}
