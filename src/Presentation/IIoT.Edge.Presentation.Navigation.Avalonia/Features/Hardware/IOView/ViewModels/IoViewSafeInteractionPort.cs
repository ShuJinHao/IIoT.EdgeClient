namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;

public interface IIoViewSafeInteractionPort
{
    Task<IoViewReadResult> ReadAsync(IoNetworkDeviceModel device, CancellationToken cancellationToken);

    Task<IoViewWriteResult> WriteAsync(
        IoNetworkDeviceModel device,
        IoInteractionRowModel row,
        int value,
        CancellationToken cancellationToken);
}

public sealed record IoViewReadResult(bool ShouldRefreshPreview, string? ErrorMessage = null);

public sealed record IoViewWriteResult(bool Accepted, string? ErrorMessage = null);

/// <summary>
/// Avalonia 迁移阶段的安全默认端口，不连接真实 PLC，只允许页面和 Headless 测试验证命令入口。
/// </summary>
public sealed class NoopIoViewSafeInteractionPort : IIoViewSafeInteractionPort
{
    public Task<IoViewReadResult> ReadAsync(IoNetworkDeviceModel device, CancellationToken cancellationToken)
        => Task.FromResult(new IoViewReadResult(ShouldRefreshPreview: true));

    public Task<IoViewWriteResult> WriteAsync(
        IoNetworkDeviceModel device,
        IoInteractionRowModel row,
        int value,
        CancellationToken cancellationToken)
        => Task.FromResult(new IoViewWriteResult(Accepted: true));
}
