using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Shell.Features.SysMenu;

/// <summary>
/// 系统菜单项视图模型。
/// 负责展示菜单信息和当前权限可访问状态。
/// </summary>
public class MenuItemViewModel : BaseControlNotifyPropertyChanged
{
    private readonly IClientPermissionService _permissionService;
    private readonly string _requiredPermission;

    public string Title { get; }
    public string ViewId { get; }
    public string Icon { get; }

    private bool _isEnabled;
    public new bool IsEnabled
    {
        get => _isEnabled;
        private set { _isEnabled = value; OnPropertyChanged(); }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public MenuItemViewModel(MenuInfo info, IClientPermissionService permissionService)
    {
        _permissionService = permissionService;
        _requiredPermission = info.RequiredPermission;

        Title = info.Title;
        ViewId = info.ViewId;
        Icon = info.Icon;

        RefreshPermission();
    }

    private bool _isAccessible;
    public bool IsAccessible
    {
        get => _isAccessible;
        private set { _isAccessible = value; OnPropertyChanged(); }
    }

    public void RefreshPermission()
    {
        IsAccessible = string.IsNullOrEmpty(_requiredPermission)
            || _permissionService.HasPermission(_requiredPermission);
        IsEnabled = IsAccessible;
    }
}
