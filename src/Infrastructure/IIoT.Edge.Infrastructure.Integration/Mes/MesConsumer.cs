using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Integration;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Infrastructure.Integration.Mes;

public sealed class MesConsumer : IMesConsumer
{
    private readonly IDeviceService _deviceService;
    private readonly IMesUploadGate _uploadGate;
    private readonly ILogService _logger;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly Dictionary<string, IProcessMesUploader> _uploaders;

    public string Name => "MES";
    public int Order => 20;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;
    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.Mes;

    public MesConsumer(
        IDeviceService deviceService,
        IMesUploadGate uploadGate,
        IEnumerable<IProcessMesUploader> uploaders,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ILogService logger)
    {
        _deviceService = deviceService;
        _uploadGate = uploadGate;
        _diagnosticsStore = diagnosticsStore;
        _logger = logger;
        _uploaders = uploaders.ToDictionary(x => x.ProcessType, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
    {
        if (!_uploaders.TryGetValue(record.CellData.ProcessType, out var uploader))
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
            _diagnosticsStore.RecordFailure(record.CellData.ProcessType, reason);
            _logger.Warn($"[MES] 上传门控未就绪（{reason}），本次数据转入 MES 补偿队列。ProcessType={record.CellData.ProcessType}");
            return false;
        }

        var device = _deviceService.CurrentDevice;
        if (device is null)
        {
            const string reason = "Device is not identified yet.";
            _diagnosticsStore.RecordFailure(record.CellData.ProcessType, reason);
            _logger.Warn($"[MES] {reason} ProcessType={record.CellData.ProcessType}");
            return false;
        }

        var result = await uploader
            .UploadAsync(new ProcessMesUploadContext(device), [record], cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _diagnosticsStore.RecordSuccess(record.CellData.ProcessType);
            return true;
        }

        _diagnosticsStore.RecordFailure(record.CellData.ProcessType, result.Message);
        _logger.Error(
            $"[MES] Upload failed for process type {record.CellData.ProcessType}. Outcome:{result.Outcome}, Message:{result.Message}");
        return false;
    }
}
