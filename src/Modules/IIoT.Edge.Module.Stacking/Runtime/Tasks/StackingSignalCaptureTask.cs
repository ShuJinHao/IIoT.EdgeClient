using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Config.Hardware;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.Stacking.Runtime.Tasks;

/// <summary>
/// 叠片 PLC 采集任务。读取序号、层数和结果码，序号递增时生成一条 StackingCellData 并进入 DataPipeline。
/// </summary>
public sealed class StackingSignalCaptureTask : PlcTaskBase
{
    private readonly IDataPipelineService _pipelineService;
    private readonly ILogicalSignalAccessor<StackingSignal> _signals;

    /// <summary>
    /// 叠片信号采集任务名称，用于运行日志和任务诊断。
    /// </summary>
    public override string TaskName => StackingModuleConstants.RuntimeTaskName;

    /// <summary>
    /// 叠片样本任务以较短周期观察 PLC 序号变化，真实现场可按模块配置扩展。
    /// </summary>
    protected override int TaskLoopInterval => 50;

    public StackingSignalCaptureTask(
        IPlcBuffer buffer,
        ILogicalSignalAccessor<StackingSignal> signals,
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

        var sequence = _signals.ReadUInt16(StackingSignal.工序序号);
        var layerCount = _signals.ReadUInt16(StackingSignal.叠片层数);
        var resultCode = ParseResultCode(_signals.ReadUInt16(StackingSignal.结果码));
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
            // PLC 序号没有递增时不重复发布，避免同一叠片记录反复进入 Cloud/MES 补偿链路。
            return;
        }

        var barcode = $"{Context.DeviceName}-ST-{sequence:D4}";
        var cellData = new StackingCellData
        {
            Barcode = barcode,
            TrayCode = $"{Context.DeviceName}-TRAY",
            LayerCount = layerCount,
            SequenceNo = sequence,
            RuntimeStatus = "已采集",
            DeviceName = Context.DeviceName,
            DeviceCode = Context.DeviceName,
            PlcDeviceId = Context.DeviceId,
            CellResult = ToCellResult(resultCode),
            CompletedTime = observedAt
        };

        Context.AddCell(barcode, cellData);
        Context.Set(StackingModuleConstants.LastPublishedSequenceKey, (int)sequence);
        Context.Set(StackingModuleConstants.LastPublishedBarcodeKey, barcode);
        _signals.WriteUInt16(StackingSignal.采集应答, sequence);

        var enqueueResult = await _pipelineService
            .EnqueueAsync(new CellCompletedRecord { CellData = cellData }, TaskCancellationToken)
            .ConfigureAwait(false);

        if (enqueueResult.WasOverflow)
        {
            Logger.Warn(
                $"[{Context.DeviceName}] {TaskName} 叠片样本 #{sequence}（{barcode}）进入溢出持久化，目标数：{enqueueResult.PersistedTargetCount}，跳过尽力目标：{enqueueResult.SkippedBestEffortCount}。");
        }

        Logger.Info(
            $"[{Context.DeviceName}] {TaskName} 已采集叠片样本 #{sequence}（{barcode}），层数：{layerCount}，结果码：{resultCode}。");
    }

    private static StackingResultCode ParseResultCode(ushort rawValue)
        => Enum.IsDefined(typeof(StackingResultCode), (int)rawValue)
            ? (StackingResultCode)rawValue
            : StackingResultCode.Unknown;

    private static bool? ToCellResult(StackingResultCode resultCode)
        => resultCode switch
        {
            StackingResultCode.Ok => true,
            StackingResultCode.Ng => false,
            _ => null
        };
}
