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
        if (!record.CellData.UploadTargets.HasFlag(DataPipelineUploadTargets.Mes))
        {
            return true;
        }

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
            var deviceName = record.ResolveDeviceName();
            var reason = string.IsNullOrWhiteSpace(gate.ReasonCode)
                ? "mes_upload_gate_blocked"
                : gate.ReasonCode;
            _diagnosticsStore.RecordBlocked(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Warn($"[PLC-{deviceName}][MES] 上传门控未就绪（{reason}），本次数据转入 MES 补偿队列。工序={processType}");
            return false;
        }

        var device = ResolveUploadDevice(record, CurrentDevice);
        if (device is null)
        {
            const string reason = "尚未识别当前设备。";
            _diagnosticsStore.RecordBlocked(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Warn($"[PLC-{record.ResolveDeviceName()}][MES] {reason} 工序={processType}");
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

        var result = await uploader
            .UploadAsync(new ProcessUploadContext(device), [record], cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _diagnosticsStore.RecordSuccess(processType, UploadDiagnosticsContextFactory.CreateMesContext(record));
            Logger.Info($"[PLC-{device.DeviceName}][MES] 工序 {processType} 上传成功。");
            return true;
        }

        _diagnosticsStore.RecordFailure(processType, result.Message, UploadDiagnosticsContextFactory.CreateMesContext(record));
        Logger.Error(
            $"[PLC-{device.DeviceName}][MES] 工序 {processType} 上传失败，结果：{result.Outcome}，原因：{result.Message}");
        return false;
    }

    private static DeviceSession? ResolveUploadDevice(CellCompletedRecord record, DeviceSession? currentDevice)
    {
        if (currentDevice is null)
        {
            return null;
        }

        var deviceName = record.ResolveDeviceName();
        return string.IsNullOrWhiteSpace(deviceName)
            ? currentDevice
            : currentDevice with { DeviceName = deviceName };
    }
}
