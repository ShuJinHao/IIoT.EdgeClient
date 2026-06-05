using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.Module.Sdk.Base;
using Microsoft.Extensions.Options;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Module.Homogenization.Runtime.Tasks;

/// <summary>
/// 匀浆握手类任务基类，统一提供 PLC 信号码表、循环间隔和 MES 结果到 PLC 应答码的转换。
/// </summary>
internal abstract class HomogenizationTaskBase : PlcTaskBase
{
    protected HomogenizationTaskBase(
        IPlcBuffer buffer,
        HomogenizationPlcHandshakeAccessor interaction,
        HomogenizationSignalCodec codec,
        HomogenizationContext context,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IOptions<HomogenizationCodeOptions> codeOptions,
        IOptions<HomogenizationModuleOptions> moduleOptions)
        : base(buffer, context, logger)
    {
        ModuleContext = context;
        ProductionTime = productionTime;
        CodeOptions = codeOptions.Value;
        var runtime = moduleOptions.Value.Runtime;
        EventLoopInterval = Math.Max(runtime.MinEventLoopIntervalMs, runtime.EventLoopIntervalMs);
        Interaction = interaction;
        Codec = codec;
    }

    /// <summary>
    /// 匀浆运行态上下文，用于记录最近一次业务结果并供 UI 展示。
    /// </summary>
    protected HomogenizationContext ModuleContext { get; }

    /// <summary>
    /// 匀浆任务使用的统一生产业务时间服务，避免对外状态和 payload 混用 UTC/本地时间。
    /// </summary>
    protected IProductionTimeProvider ProductionTime { get; }

    /// <summary>
    /// 匀浆 PLC/MES/Cloud code 配置，包含触发码、复位码、应答码、MES 通道和 Cloud 日志映射。
    /// </summary>
    protected HomogenizationCodeOptions CodeOptions { get; }

    /// <summary>
    /// 触发-应答任务循环间隔，已按配置的最小值保护。
    /// </summary>
    protected int EventLoopInterval { get; }

    /// <summary>
    /// 匀浆信号交互访问器，任务通过它表达触发、应答和复位，不直接比较 PLC code。
    /// </summary>
    protected HomogenizationPlcHandshakeAccessor Interaction { get; }

    /// <summary>
    /// 匀浆业务快照解码器，只负责把 PLC 连续区转换为插件 payload。
    /// </summary>
    protected HomogenizationSignalCodec Codec { get; }

    /// <summary>
    /// PLC 任务循环间隔，来源于匀浆运行配置。
    /// </summary>
    protected override int TaskLoopInterval => EventLoopInterval;

    protected static string FormatDuplicateTrayMessage(HomogenizationTrayCodeStage stage, string trayCode)
    {
        var stageName = stage == HomogenizationTrayCodeStage.Inbound ? "进站" : "出站";
        return $"托盘码重复，已按业务 NG 拒绝{stageName}：{trayCode.Trim()}。";
    }

    protected async Task<string?> ResolveDuplicateTrayMessageAsync(
        IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business> parameters,
        HomogenizationTrayCodeStage stage,
        string trayCode,
        CancellationToken cancellationToken)
    {
        var snapshot = await parameters.GetAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Business<bool>(HomogenizationParams.Business.启用托盘码重码验证)
               && ModuleContext.HasProcessedTray(stage, trayCode)
            ? FormatDuplicateTrayMessage(stage, trayCode)
            : null;
    }

    protected async Task ExecuteHandshakeAsync(
        HomogenizationPlcSignals.Interaction trigger,
        string triggeredLog,
        string resetLog,
        Func<CancellationToken, Task> processTriggerAsync,
        Func<Exception, string> createExceptionMessage,
        Action<string> recordException)
    {
        switch (Step)
        {
            case 0:
                if (Interaction.IsTriggered(trigger))
                {
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} {triggeredLog}");
                    Step = 10;
                }

                break;

            case 10:
                try
                {
                    await processTriggerAsync(TaskCancellationToken).ConfigureAwait(false);
                    Step = 30;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var message = createExceptionMessage(ex);
                    recordException(message);
                    Interaction.ReplyException(trigger);
                    Logger.Error($"[{ModuleContext.DeviceName}] {TaskName} {message}");
                    Step = 30;
                }

                break;

            case 30:
                if (Interaction.IsReset(trigger))
                {
                    Interaction.ReplyReset(trigger);
                    Logger.Info($"[{ModuleContext.DeviceName}] {TaskName} {resetLog}");
                    Step = 0;
                }

                break;
        }
    }

    protected Task ExecuteMesSnapshotHandshakeAsync<TSnapshot>(
        HomogenizationPlcSignals.Interaction trigger,
        string triggeredLog,
        string resetLog,
        string exceptionPrefix,
        string channel,
        IHomogenizationProductionGate productionGate,
        IMesUploadDiagnosticsStore diagnosticsStore,
        Func<TSnapshot> captureSnapshot,
        Func<TSnapshot, CancellationToken, Task<MesCallResult>> uploadAsync,
        Action<string> recordFailure,
        Action<TSnapshot, MesCallResult> recordResult,
        Action<TSnapshot>? beforeUpload = null)
        => ExecuteHandshakeAsync(
            trigger,
            triggeredLog,
            resetLog,
            _ => UploadMesSnapshotWithGateAsync(
                trigger,
                channel,
                productionGate,
                diagnosticsStore,
                captureSnapshot,
                uploadAsync,
                recordFailure,
                recordResult,
                beforeUpload),
            ex => $"{exceptionPrefix}：{ex.Message}",
            message =>
            {
                diagnosticsStore.RecordFailure(channel, message);
                recordFailure(message);
            });

    protected async Task UploadMesSnapshotWithGateAsync<TSnapshot>(
        HomogenizationPlcSignals.Interaction trigger,
        string channel,
        IHomogenizationProductionGate productionGate,
        IMesUploadDiagnosticsStore diagnosticsStore,
        Func<TSnapshot> captureSnapshot,
        Func<TSnapshot, CancellationToken, Task<MesCallResult>> uploadAsync,
        Action<string> recordGateFailure,
        Action<TSnapshot, MesCallResult> recordResult,
        Action<TSnapshot>? beforeUpload = null)
    {
        var gateResult = await productionGate.EnsureReadyAsync(ModuleContext, TaskCancellationToken).ConfigureAwait(false);
        if (!gateResult.IsSuccess)
        {
            diagnosticsStore.RecordFailure(channel, gateResult.Message);
            recordGateFailure(gateResult.Message);
            Interaction.ReplyResult(trigger, gateResult);
            return;
        }

        var snapshot = captureSnapshot();
        beforeUpload?.Invoke(snapshot);
        var result = await uploadAsync(snapshot, TaskCancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            diagnosticsStore.RecordSuccess(channel);
        }
        else
        {
            diagnosticsStore.RecordFailure(channel, result.Message);
        }

        recordResult(snapshot, result);
        Interaction.ReplyResult(trigger, result);
    }

}
