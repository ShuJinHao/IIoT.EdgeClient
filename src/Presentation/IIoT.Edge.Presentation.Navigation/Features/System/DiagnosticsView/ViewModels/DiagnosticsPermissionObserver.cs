using Avalonia.Threading;
using IIoT.Edge.Application.Abstractions.Auth;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public interface IDiagnosticsPermissionObserver
{
    bool CanOperateDeadLetters { get; }

    void Start();

    void Stop();
}

internal sealed class DiagnosticsPermissionObserver(
    IClientPermissionService permissionService,
    IDiagnosticsViewModelCallback callback)
    : IDiagnosticsPermissionObserver
{
    public bool CanOperateDeadLetters => permissionService.IsLocalAdmin;

    public void Start()
        => permissionService.PermissionStateChanged += HandlePermissionStateChanged;

    public void Stop()
        => permissionService.PermissionStateChanged -= HandlePermissionStateChanged;

    private void HandlePermissionStateChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            callback.RefreshPermissionState();
            return;
        }

        Dispatcher.UIThread.Post(callback.RefreshPermissionState);
    }
}
