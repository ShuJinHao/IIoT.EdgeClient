using IIoT.Edge.Application.Abstractions.Config;
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
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly ILogService _logger;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly IExternalHeartbeatStateStore? _heartbeatStore;
    private readonly Dictionary<string, IProcessMesUploader> _uploaders;

    public string Name => "MES";
    public int Order => 20;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;
    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.Mes;

    public MesConsumer(
        IDeviceService deviceService,
        ILocalSystemRuntimeConfigService runtimeConfig,
        IEnumerable<IProcessMesUploader> uploaders,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ILogService logger,
        IExternalHeartbeatStateStore? heartbeatStore = null)
    {
        _deviceService = deviceService;
        _runtimeConfig = runtimeConfig;
        _diagnosticsStore = diagnosticsStore;
        _logger = logger;
        _heartbeatStore = heartbeatStore;
        _uploaders = uploaders.ToDictionary(x => x.ProcessType, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
    {
        if (!_uploaders.TryGetValue(record.CellData.ProcessType, out var uploader))
        {
            return true;
        }

        if (!_runtimeConfig.Current.MesUploadEnabled)
        {
            return true;
        }

        var heartbeat = _heartbeatStore?.Get(ExternalSystemKind.Mes);
        if (heartbeat is not null && !heartbeat.IsReady)
        {
            var reason = string.IsNullOrWhiteSpace(heartbeat.ReasonCode)
                ? "mes_heartbeat_not_ready"
                : heartbeat.ReasonCode;
            _diagnosticsStore.RecordFailure(record.CellData.ProcessType, reason);
            _logger.Warn($"[MES] 心跳未就绪（{reason}），本次数据转入 MES 补偿队列。ProcessType={record.CellData.ProcessType}");
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
