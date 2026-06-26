using Avalonia.Threading;
using IIoT.Edge.Application.Abstractions.Auth;
using System.Threading;

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
    private int _isActive;

    public bool CanOperateDeadLetters => permissionService.IsLocalAdmin;

    public void Start()
    {
        if (Interlocked.Exchange(ref _isActive, 1) == 1)
        {
            return;
        }

        permissionService.PermissionStateChanged += HandlePermissionStateChanged;
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _isActive, 0) == 0)
        {
            return;
        }

        permissionService.PermissionStateChanged -= HandlePermissionStateChanged;
    }

    private void HandlePermissionStateChanged()
    {
        if (Volatile.Read(ref _isActive) == 0)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshIfActive();
            return;
        }

        Dispatcher.UIThread.Post(RefreshIfActive);
    }

    private void RefreshIfActive()
    {
        if (Volatile.Read(ref _isActive) == 0)
        {
            return;
        }

        callback.RefreshPermissionState();
    }
}
