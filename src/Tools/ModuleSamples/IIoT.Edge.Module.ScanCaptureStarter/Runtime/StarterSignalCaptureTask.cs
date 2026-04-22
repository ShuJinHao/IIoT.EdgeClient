using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.ScanCaptureStarter.Constants;
using IIoT.Edge.Module.ScanCaptureStarter.Payload;
using IIoT.Edge.Plugin.Shared.Signals;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.ScanCaptureStarter.Runtime;

public sealed class StarterSignalCaptureTask : PlcTaskBase
{
    private readonly IDataPipelineService _pipelineService;
    private readonly ILogicalSignalAccessor _signals;

    public override string TaskName => StarterModuleConstants.SignalTaskName;

    protected override int TaskLoopInterval => 50;

    public StarterSignalCaptureTask(
        IPlcBuffer buffer,
        ILogicalSignalAccessor signals,
        ProductionContext context,
        IDataPipelineService pipelineService,
        ILogService logger)
        : base(buffer, context, logger)
    {
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
        _pipelineService = pipelineService;
    }

    protected override async Task DoCoreAsync()
    {
        Context.Set(StarterModuleConstants.RuntimeRegisteredKey, true);

        var sequence = _signals.Read(StarterPlcSignalProfile.ReadSignals[1].Label);
        var resultCode = _signals.Read(StarterPlcSignalProfile.ReadSignals[2].Label);
        var observedAt = DateTime.UtcNow;

        Context.Set(StarterModuleConstants.LastObservedSequenceKey, (int)sequence);
        Context.Set(StarterModuleConstants.LastObservedResultCodeKey, (int)resultCode);
        Context.Set(StarterModuleConstants.LastObservedAtKey, observedAt);

        if (sequence == 0)
        {
            return;
        }

        var pendingBarcode = Context.Get<string>(StarterModuleConstants.PendingBarcodeKey);
        if (string.IsNullOrWhiteSpace(pendingBarcode))
        {
            return;
        }

        var lastPublishedSequence = Context.Get<int>(StarterModuleConstants.LastPublishedSequenceKey);
        if (sequence <= lastPublishedSequence)
        {
            return;
        }

        var cellData = new StarterCellData
        {
            Barcode = pendingBarcode,
            SequenceNo = sequence,
            RuntimeStatus = "Captured",
            DeviceName = Context.DeviceName,
            DeviceCode = Context.DeviceName,
            PlcDeviceId = Context.DeviceId,
            CellResult = StarterPlcSignalProfile.ToCellResult(resultCode),
            CompletedTime = observedAt
        };

        Context.AddCell(pendingBarcode, cellData);
        Context.Set(StarterModuleConstants.LastPublishedSequenceKey, (int)sequence);
        Context.Set(StarterModuleConstants.LastPublishedBarcodeKey, pendingBarcode);
        Context.Set(StarterModuleConstants.LastPublishedAtKey, observedAt);
        _signals.Write(StarterPlcSignalProfile.WriteSignals[1].Label, sequence);

        var enqueueResult = await _pipelineService
            .EnqueueAsync(new CellCompletedRecord { CellData = cellData }, TaskCancellationToken)
            .ConfigureAwait(false);

        Context.RemoveDeviceData(StarterModuleConstants.PendingBarcodeKey);
        Context.RemoveDeviceData(StarterModuleConstants.PendingBarcodeObservedAtKey);

        if (enqueueResult.WasOverflow)
        {
            Logger.Warn(
                $"[{Context.DeviceName}] {TaskName} overflow persisted sample #{sequence} ({pendingBarcode}). Targets:{enqueueResult.PersistedTargetCount}, SkippedBestEffort:{enqueueResult.SkippedBestEffortCount}");
        }

        Logger.Info(
            $"[{Context.DeviceName}] {TaskName} captured sample #{sequence} ({pendingBarcode}), ResultCode:{resultCode}.");
    }
}
