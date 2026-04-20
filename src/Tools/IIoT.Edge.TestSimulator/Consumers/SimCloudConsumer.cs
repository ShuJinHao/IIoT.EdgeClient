using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.Device;

namespace IIoT.Edge.TestSimulator.Consumers;

/// <summary>
/// Test cloud consumer for simulator.
/// Uses FakeHttpClient and supports both single and batch upload paths.
/// </summary>
public sealed class SimCloudConsumer : ICloudConsumer, ICloudBatchConsumer
{
    private readonly ICloudHttpClient _httpClient;
    private readonly IDeviceService _deviceService;
    private readonly ILogService _logger;

    public string Name => "Cloud";
    public int Order => 30;
    public IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode FailureMode
        => IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode.Durable;
    public string? RetryChannel => "Cloud";

    public SimCloudConsumer(
        ICloudHttpClient httpClient,
        IDeviceService deviceService,
        ILogService logger)
    {
        _httpClient = httpClient;
        _deviceService = deviceService;
        _logger = logger;
    }

    public async Task<bool> ProcessAsync(CellCompletedRecord record)
        => (await ProcessWithResultAsync(record).ConfigureAwait(false)).IsSuccess;

    public Task<CloudCallResult> ProcessWithResultAsync(CellCompletedRecord record)
        => ProcessBatchAsync([record]);

    public async Task<CloudCallResult> ProcessBatchAsync(IReadOnlyList<CellCompletedRecord> records)
    {
        if (records.Count == 0)
            return CloudCallResult.Success();

        if (!_deviceService.CanUploadToCloud)
        {
            _logger.Warn($"[SimCloud] Offline. {records.Count} records queued for retry");
            return CloudCallResult.Failure(CloudCallOutcome.SkippedUploadNotReady, "simulator_gate_blocked");
        }

        var device = _deviceService.CurrentDevice;
        if (device is null)
        {
            _logger.Warn("[SimCloud] Device not identified. Queue record(s) for retry");
            return CloudCallResult.Failure(CloudCallOutcome.SkippedUploadNotReady, "simulator_device_missing");
        }

        var payload = new
        {
            deviceId = device.DeviceId,
            items = records.Select(r => r.CellData).ToArray()
        };

        var result = await _httpClient.PostAsync("/api/test/passstation/batch", payload);
        if (result.IsSuccess)
            _logger.Info($"[SimCloud] Batch upload success: {records.Count}");
        else
            _logger.Error($"[SimCloud] Batch upload failed: {records.Count}, reason={result.ReasonCode}");

        return result;
    }
}

