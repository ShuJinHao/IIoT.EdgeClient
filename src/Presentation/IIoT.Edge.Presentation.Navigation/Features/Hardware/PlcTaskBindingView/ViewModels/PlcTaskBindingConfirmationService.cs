using System.Windows;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.PlcTaskBindingView;

public interface IPlcTaskBindingConfirmationService
{
    bool ConfirmDisableHeartbeat(string deviceName, IReadOnlyCollection<string> taskNames);
}

public sealed class PlcTaskBindingConfirmationService(IAppLanguageService languageService) : IPlcTaskBindingConfirmationService
{
    public bool ConfirmDisableHeartbeat(string deviceName, IReadOnlyCollection<string> taskNames)
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

        return MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
