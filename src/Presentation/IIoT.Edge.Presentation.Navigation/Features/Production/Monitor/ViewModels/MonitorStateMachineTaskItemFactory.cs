using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

public interface IMonitorStateMachineTaskItemFactory
{
    IReadOnlyList<MonitorStateMachineTaskItemViewModel> CreateItems(IReadOnlyList<MonitorStateMachineTaskSnapshot> rows);
}

internal sealed class MonitorStateMachineTaskItemFactory(IAppLanguageService languageService)
    : IMonitorStateMachineTaskItemFactory
{
    public IReadOnlyList<MonitorStateMachineTaskItemViewModel> CreateItems(IReadOnlyList<MonitorStateMachineTaskSnapshot> rows)
        => rows
            .Where(static row => row.Enabled)
            .Select(Create)
            .ToArray();

    private MonitorStateMachineTaskItemViewModel Create(MonitorStateMachineTaskSnapshot snapshot)
    {
        var availabilityStatusText = snapshot.CanRun
            ? GetText("Navigation_Monitor_StateMachineTaskRunnable", "可运行")
            : GetText("Navigation_Monitor_StateMachineTaskUnavailable", "不可运行");
        var detailText = snapshot.CanRun
            ? snapshot.IsHeartbeatLike
                ? GetText("Navigation_Monitor_StateMachineHeartbeatTask", "心跳类任务")
                : GetText("Navigation_Monitor_StateMachineRuntimeTask", "插件运行任务")
            : string.IsNullOrWhiteSpace(snapshot.UnavailableReason)
                ? GetText("Navigation_Monitor_StateMachineTaskUnavailable", "不可运行")
                : snapshot.UnavailableReason;

        return new MonitorStateMachineTaskItemViewModel(
            snapshot.DisplayName,
            availabilityStatusText,
            FormatStepText(snapshot.StepValue),
            snapshot.StepValue?.ToString() ?? GetText("Navigation_Monitor_NoTaskStep", "暂无步骤"),
            detailText,
            !snapshot.CanRun);
    }

    private string FormatStepText(int? stepValue)
        => stepValue switch
        {
            null => GetText("Navigation_Monitor_StateMachineStepNone", "暂无步骤状态"),
            0 => GetText("Navigation_Monitor_StateMachineStepWaiting", "等待触发"),
            10 => GetText("Navigation_Monitor_StateMachineStepProcessing", "处理中"),
            30 => GetText("Navigation_Monitor_StateMachineStepWaitingReset", "等待 PLC 复位"),
            _ => languageService.Format(
                "Navigation_Monitor_StateMachineStepFormat",
                "步骤 {0}",
                stepValue.Value)
        };

    private string GetText(string resourceKey, string fallback)
        => languageService.GetString(resourceKey, fallback);
}
