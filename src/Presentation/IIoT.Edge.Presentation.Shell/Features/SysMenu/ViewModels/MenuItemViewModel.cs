using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.UI.Shared.Localization;
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
    private readonly IAppLanguageService _languageService;
    private readonly string _requiredPermission;
    private readonly string _fallbackTitle;
    private readonly string _titleResourceKey;
    private string _title;

    public MenuItemViewModel(
        MenuInfo info,
        IClientPermissionService permissionService,
        IAppLanguageService languageService)
    {
        _permissionService = permissionService;
        _languageService = languageService;
        _requiredPermission = info.RequiredPermission;
        _fallbackTitle = info.Title;
        _titleResourceKey = info.TitleResourceKey;
        _title = ResolveTitle();

        ViewId = info.ViewId;
        Icon = info.Icon;

        RefreshPermission();
    }

    public string Title
    {
        get => _title;
        private set { _title = value; OnPropertyChanged(); }
    }

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

    public void RefreshTitle()
        => Title = ResolveTitle();

    private string ResolveTitle()
    {
        if (!string.IsNullOrWhiteSpace(_titleResourceKey))
        {
            return _languageService.GetString(_titleResourceKey, _fallbackTitle);
        }

        return string.IsNullOrWhiteSpace(_fallbackTitle)
            ? _titleResourceKey
            : _fallbackTitle;
    }
}
