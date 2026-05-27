using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

public sealed class MonitorTabItemViewModel : BaseNotifyPropertyChanged
{
    private readonly IAppLanguageService _languageService;
    private bool _isSelected;

    public MonitorTabItemViewModel(
        IAppLanguageService languageService,
        string key,
        string titleResourceKey,
        string titleFallback)
    {
        _languageService = languageService;
        Key = key;
        TitleResourceKey = titleResourceKey;
        TitleFallback = titleFallback;
    }

    public string Key { get; }

    public string TitleResourceKey { get; }

    public string TitleFallback { get; }

    public string Title => _languageService.GetString(TitleResourceKey, TitleFallback);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public void RefreshLanguage()
        => OnPropertyChanged(nameof(Title));

    public override string ToString()
        => Title;
}

public sealed class MonitorCellDebugItemViewModel
{
    public MonitorCellDebugItemViewModel(MonitorCellDebugSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public MonitorCellDebugSnapshot Snapshot { get; }

    public string DeviceName => Snapshot.DeviceName;

    public string InternalKey => Snapshot.InternalKey;

    public string DisplayLabel => Snapshot.DisplayLabel;

    public string ProcessType => Snapshot.ProcessType;

    public string RuntimeStatusText => Snapshot.RuntimeStatusText;

    public string CompletedTimeText => Snapshot.CompletedTimeText;

    public IReadOnlyList<MonitorSnapshotRow> FieldRows => Snapshot.FieldRows;
}

public sealed record MonitorStatusItemVm(string Label, string Value);

public sealed record MonitorStateMachineTaskItemViewModel(
    string DisplayName,
    string AvailabilityStatusText,
    string StepText,
    string StepValueText,
    string DetailText,
    bool IsUnavailable)
{
    public static MonitorStateMachineTaskItemViewModel Create(
        MonitorStateMachineTaskSnapshot snapshot,
        Func<string, string, string> getText)
    {
        var availabilityStatusText = snapshot.CanRun
            ? getText("Navigation_Monitor_StateMachineTaskRunnable", "可运行")
            : getText("Navigation_Monitor_StateMachineTaskUnavailable", "不可运行");
        var detailText = snapshot.CanRun
            ? snapshot.IsHeartbeatLike
                ? getText("Navigation_Monitor_StateMachineHeartbeatTask", "心跳类任务")
                : getText("Navigation_Monitor_StateMachineRuntimeTask", "插件运行任务")
            : string.IsNullOrWhiteSpace(snapshot.UnavailableReason)
                ? getText("Navigation_Monitor_StateMachineTaskUnavailable", "不可运行")
                : snapshot.UnavailableReason;

        return new MonitorStateMachineTaskItemViewModel(
            snapshot.DisplayName,
            availabilityStatusText,
            FormatStepText(snapshot.StepValue, getText),
            snapshot.StepValue?.ToString() ?? getText("Navigation_Monitor_NoTaskStep", "暂无步骤"),
            detailText,
            !snapshot.CanRun);
    }

    private static string FormatStepText(int? stepValue, Func<string, string, string> getText)
        => stepValue switch
        {
            null => getText("Navigation_Monitor_StateMachineStepNone", "暂无步骤状态"),
            0 => getText("Navigation_Monitor_StateMachineStepWaiting", "等待触发"),
            10 => getText("Navigation_Monitor_StateMachineStepProcessing", "处理中"),
            30 => getText("Navigation_Monitor_StateMachineStepWaitingReset", "等待 PLC 复位"),
            _ => string.Format(
                getText("Navigation_Monitor_StateMachineStepFormat", "步骤 {0}"),
                stepValue.Value)
        };
}
