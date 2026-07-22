using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public interface IDiagnosticsDeadLetterConfirmationService
{
    Task<bool> ConfirmRequeueAsync(DeadLetterRow row);

    Task<bool> ConfirmDeleteAsync(DeadLetterRow row);
}

public sealed class DiagnosticsDeadLetterConfirmationService(IAppLanguageService languageService)
    : IDiagnosticsDeadLetterConfirmationService
{
    public Task<bool> ConfirmRequeueAsync(DeadLetterRow row)
    {
        var message = languageService.Format(
            "Navigation_Diagnostics_ConfirmRequeueMessageFormat",
            "即将把{0}死信记录重新写入对应 retry 队列。ID：{1}；工序：{2}；目标：{3}。成功后该记录会从死信表移除，后续由正常补传任务处理。是否继续？",
            FormatChannel(row.Channel),
            row.Id,
            row.ProcessType,
            row.FailedTarget);
        var title = languageService.GetString(
            "Navigation_Diagnostics_ConfirmRequeueTitle",
            "确认重新入队");

        return ConfirmAsync(title, message);
    }

    public Task<bool> ConfirmDeleteAsync(DeadLetterRow row)
    {
        var message = languageService.Format(
            "Navigation_Diagnostics_ConfirmDeleteMessageFormat",
            "即将删除{0}死信记录。ID：{1}；工序：{2}；目标：{3}。删除只会移除本地死信记录，不会补传，且不可恢复。是否继续？",
            FormatChannel(row.Channel),
            row.Id,
            row.ProcessType,
            row.FailedTarget);
        var title = languageService.GetString(
            "Navigation_Diagnostics_ConfirmDeleteTitle",
            "确认删除死信");

        return ConfirmAsync(title, message);
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            var result = new TaskCompletionSource<bool>();
            Dispatcher.UIThread.Post(async () =>
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

        var dialog = new DiagnosticsConfirmationDialog(title, message);
        return await dialog.ShowDialog<bool>(owner);
    }

    private string FormatChannel(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => languageService.GetString("Navigation_Diagnostics_ChannelCloud", "云端"),
            DataPipelineRetryChannel.Mes => languageService.GetString("Navigation_Diagnostics_ChannelMes", "MES"),
            _ => channel.ToString()
        };
}
