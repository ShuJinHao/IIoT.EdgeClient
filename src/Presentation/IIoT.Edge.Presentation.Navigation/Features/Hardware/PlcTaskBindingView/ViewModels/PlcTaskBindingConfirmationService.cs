using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using IIoT.Edge.Presentation.Navigation.Features.Hardware;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.PlcTaskBindingView;

public interface IPlcTaskBindingConfirmationService
{
    Task<bool> ConfirmDisableHeartbeatAsync(string deviceName, IReadOnlyCollection<string> taskNames);
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
