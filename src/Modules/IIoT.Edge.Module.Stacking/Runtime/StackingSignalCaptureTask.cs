using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.Stacking.Runtime;

public sealed class StackingSignalCaptureTask : PlcTaskBase
{
    private readonly IDataPipelineService _pipelineService;
    private readonly ILogicalSignalAccessor _signals;

    public override string TaskName => StackingModuleConstants.RuntimeTaskName;

    protected override int TaskLoopInterval => 50;

    public StackingSignalCaptureTask(
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
        Context.Set(StackingModuleConstants.RuntimeRegisteredKey, true);

        var sequence = _signals.Read(StackingPlcSignalProfile.Sequence.Label);
        var layerCount = _signals.Read(StackingPlcSignalProfile.LayerCount.Label);
        var resultCode = StackingPlcSignalProfile.ParseResultCode(
            _signals.Read(StackingPlcSignalProfile.ResultCode.Label));
        var observedAt = DateTime.UtcNow;

        Context.Set(StackingModuleConstants.LastObservedSequenceKey, (int)sequence);
        Context.Set(StackingModuleConstants.LastObservedLayerCountKey, (int)layerCount);
        Context.Set(StackingModuleConstants.LastObservedResultCodeKey, (int)resultCode);
        Context.Set(StackingModuleConstants.LastObservedAtKey, observedAt);

        if (sequence == 0)
        {
            return;
        }

        var lastPublishedSequence = Context.Get<int>(StackingModuleConstants.LastPublishedSequenceKey);
        if (sequence <= lastPublishedSequence)
        {
            return;
        }

        var barcode = $"{Context.DeviceName}-ST-{sequence:D4}";
        var cellData = new StackingCellData
        {
            Barcode = barcode,
            TrayCode = $"{Context.DeviceName}-TRAY",
            LayerCount = layerCount,
            SequenceNo = sequence,
            RuntimeStatus = "Captured",
            DeviceName = Context.DeviceName,
            DeviceCode = Context.DeviceName,
            PlcDeviceId = Context.DeviceId,
            CellResult = StackingPlcSignalProfile.ToCellResult(resultCode),
            CompletedTime = observedAt
        };

        Context.AddCell(barcode, cellData);
        Context.Set(StackingModuleConstants.LastPublishedSequenceKey, (int)sequence);
        Context.Set(StackingModuleConstants.LastPublishedBarcodeKey, barcode);
        _signals.Write(StackingPlcSignalProfile.Ack.Label, sequence);

        var enqueueResult = await _pipelineService
            .EnqueueAsync(new CellCompletedRecord { CellData = cellData }, TaskCancellationToken)
            .ConfigureAwait(false);

        if (enqueueResult.WasOverflow)
        {
            Logger.Warn(
                $"[{Context.DeviceName}] {TaskName} overflow persisted sample #{sequence} ({barcode}). Targets:{enqueueResult.PersistedTargetCount}, SkippedBestEffort:{enqueueResult.SkippedBestEffortCount}");
        }

        Logger.Info(
            $"[{Context.DeviceName}] {TaskName} captured sample #{sequence} ({barcode}), Layers:{layerCount}, ResultCode:{resultCode}.");
    }
}
