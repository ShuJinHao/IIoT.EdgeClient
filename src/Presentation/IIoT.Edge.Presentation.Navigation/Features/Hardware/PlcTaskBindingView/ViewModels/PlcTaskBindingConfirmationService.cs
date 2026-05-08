using System.Windows;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.PlcTaskBindingView;

public interface IPlcTaskBindingConfirmationService
{
    bool ConfirmDisableHeartbeat(string deviceName, IReadOnlyCollection<string> taskNames);
}

public sealed class PlcTaskBindingConfirmationService : IPlcTaskBindingConfirmationService
{
    public bool ConfirmDisableHeartbeat(string deviceName, IReadOnlyCollection<string> taskNames)
    {
        var message = $"即将关闭 PLC“{deviceName}”的心跳类任务：{string.Join("、", taskNames)}。\n关闭后该 PLC 不再执行心跳握手，可能影响运行状态判断。是否继续保存？";
        return MessageBox.Show(
            message,
            "确认关闭心跳任务",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
