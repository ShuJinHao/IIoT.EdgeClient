using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.SharedKernel.Modules.Stacking;

namespace IIoT.Edge.Runtime.Stations.Stacking.Tasks;

public sealed class StackingSignalCaptureTask : PlcTaskBase
{
    private readonly IDataPipelineService _pipelineService;

    public override string TaskName => StackingModuleConstants.RuntimeTaskName;

    protected override int TaskLoopInterval => 50;

    public StackingSignalCaptureTask(
        IPlcBuffer buffer,
        ProductionContext context,
        IDataPipelineService pipelineService,
        ILogService logger)
        : base(buffer, context, logger)
    {
        _pipelineService = pipelineService;
    }

    protected override Task DoCoreAsync()
    {
        Context.Set(StackingModuleConstants.RuntimeRegisteredKey, true);

        var sequence = Buffer.GetReadValue(StackingPlcSignalProfile.SequenceReadIndex);
        var layerCount = Buffer.GetReadValue(StackingPlcSignalProfile.LayerCountReadIndex);
        var resultCode = StackingPlcSignalProfile.ParseResultCode(
            Buffer.GetReadValue(StackingPlcSignalProfile.ResultCodeReadIndex));
        var observedAt = DateTime.Now;

        Context.Set(StackingModuleConstants.LastObservedSequenceKey, (int)sequence);
        Context.Set(StackingModuleConstants.LastObservedLayerCountKey, (int)layerCount);
        Context.Set(StackingModuleConstants.LastObservedResultCodeKey, (int)resultCode);
        Context.Set(StackingModuleConstants.LastObservedAtKey, observedAt);

        if (sequence == 0)
        {
            return Task.CompletedTask;
        }

        var lastPublishedSequence = Context.Get<int>(StackingModuleConstants.LastPublishedSequenceKey);
        if (sequence <= lastPublishedSequence)
        {
            return Task.CompletedTask;
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
        Buffer.SetWriteValue(StackingPlcSignalProfile.AckWriteIndex, sequence);

        _pipelineService.Enqueue(new CellCompletedRecord { CellData = cellData });

        Logger.Info(
            $"[{Context.DeviceName}] {TaskName} captured sample #{sequence} ({barcode}), Layers:{layerCount}, ResultCode:{resultCode}.");

        return Task.CompletedTask;
    }
}
