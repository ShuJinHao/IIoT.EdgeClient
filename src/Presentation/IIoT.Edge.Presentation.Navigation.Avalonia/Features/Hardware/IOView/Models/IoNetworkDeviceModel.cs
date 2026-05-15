namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;

/// <summary>
/// IO 交互页设备选择用模型，只暴露界面绑定所需字段。
/// </summary>
public sealed class IoNetworkDeviceModel
{
    public int Id { get; init; }

    public string DeviceName { get; init; } = string.Empty;

    public string DeviceCode { get; init; } = string.Empty;
}
