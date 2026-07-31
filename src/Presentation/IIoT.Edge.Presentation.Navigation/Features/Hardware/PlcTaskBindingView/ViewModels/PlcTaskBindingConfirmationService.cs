using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using IIoT.Edge.Module.Contracts.Plc.Checkpoints;
using IIoT.Edge.Presentation.Navigation.Features.Hardware;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.PlcTaskBindingView;

public interface IPlcTaskBindingConfirmationService
{
    Task<bool> ConfirmDisableHeartbeatAsync(string deviceName, IReadOnlyCollection<string> taskNames);

    Task<bool> ConfirmRecoveryAsync(
        string plcCode,
        string taskKey,
        string? checkpointMagazineCode,
        string? observedMagazineCode,
        PlcTaskRecoveryConfirmationAction action);
}

public sealed class PlcTaskBindingConfirmationService(IAppLanguageService languageService)
    : IPlcTaskBindingConfirmationService
{
    public Task<bool> ConfirmDisableHeartbeatAsync(string deviceName, IReadOnlyCollection<string> taskNames)
    {
        var separator = languageService.GetString("Navigation_ListSeparator", "、");
        var message = languageService.Format(
            "Navigation_PlcTaskBinding_DisableHeartbeatConfirmMessageFormat",
            "即将关闭 PLC“{0}”的心跳类任务：{1}。\n关闭后该 PLC 不再执行心跳握手，可能影响运行状态判断。是否继续保存？",
            deviceName,
            string.Join(separator, taskNames));
        var title = languageService.GetString(
            "Navigation_PlcTaskBinding_DisableHeartbeatConfirmTitle",
            "确认关闭心跳任务");

        return ConfirmAsync(title, message);
    }

    public Task<bool> ConfirmRecoveryAsync(
        string plcCode,
        string taskKey,
        string? checkpointMagazineCode,
        string? observedMagazineCode,
        PlcTaskRecoveryConfirmationAction action)
    {
        var checkpointText = string.IsNullOrWhiteSpace(checkpointMagazineCode)
            ? "--"
            : checkpointMagazineCode;
        var observedText = string.IsNullOrWhiteSpace(observedMagazineCode)
            ? languageService.GetString(
                "Navigation_PlcTaskBinding_EmptyMagazineCode",
                "空码")
            : observedMagazineCode;
        var isResume = action == PlcTaskRecoveryConfirmationAction.ResumeCheckpoint;
        var title = languageService.GetString(
            isResume
                ? "Navigation_PlcTaskBinding_ResumeCheckpointConfirmTitle"
                : "Navigation_PlcTaskBinding_AuditTerminateConfirmTitle",
            isResume ? "确认恢复检查点" : "确认审计终止旧未完成件");
        var message = isResume
            ? languageService.Format(
                "Navigation_PlcTaskBinding_ResumeCheckpointConfirmMessageFormat",
                "PLC“{0}”任务“{1}”的现场读值与检查点弹夹码一致（{2}）。确认后将恢复原检查点继续运行，是否继续？",
                plcCode,
                taskKey,
                checkpointText)
            : languageService.Format(
                "Navigation_PlcTaskBinding_AuditTerminateConfirmMessageFormat",
                "PLC“{0}”任务“{1}”的检查点弹夹码为“{2}”，现场读值为“{3}”。确认后仅审计终止旧未完成件，不生成完工、上传或产量；异码等待下一份新鲜快照，空码继续等待。是否继续？",
                plcCode,
                taskKey,
                checkpointText,
                observedText);

        return ConfirmAsync(title, message);
    }

    private static async Task<bool> ConfirmAsync(string title, string message)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            var result = new TaskCompletionSource<bool>();
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    result.SetResult(await ConfirmOnUiThreadAsync(title, message));
                }
                catch (Exception ex)
                {
                    result.SetException(ex);
                }
            });
            return await result.Task;
        }

        return await ConfirmOnUiThreadAsync(title, message);
    }

    private static async Task<bool> ConfirmOnUiThreadAsync(string title, string message)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return false;
        }

        var owner = lifetime.Windows.FirstOrDefault(static window => window.IsActive)
                    ?? lifetime.MainWindow;
        if (owner is null)
        {
            return false;
        }

        var dialog = new HardwareConfirmationDialog(title, message);
        return await dialog.ShowDialog<bool>(owner);
    }
}
