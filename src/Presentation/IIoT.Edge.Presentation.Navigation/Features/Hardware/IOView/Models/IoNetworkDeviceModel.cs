namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

/// <summary>
/// IO交互页设备选择用模型。
/// 只暴露界面所需的最小字段，不直接承载领域实体。
/// </summary>
public class IoNetworkDeviceModel
{
    public int Id { get; set; }
    public string DeviceName { get; set; } = string.Empty;
}


