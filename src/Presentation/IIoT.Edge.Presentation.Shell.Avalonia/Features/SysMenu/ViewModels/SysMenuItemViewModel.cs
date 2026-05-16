using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Modularity;

namespace IIoT.Edge.Presentation.Shell.Avalonia.Features.SysMenu.ViewModels;

public sealed partial class SysMenuItemViewModel : ObservableObject
{
    private readonly Action<string> _navigate;
    private readonly IClientPermissionService _permissionService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly string _requiredPermission;
    private readonly string _titleResourceKey;
    private readonly string _fallbackTitle;

    public SysMenuItemViewModel(
        AvaloniaMenuInfo info,
        IClientPermissionService permissionService,
        IAvaloniaLanguageService languageService,
        Action<string> navigate)
    {
        ArgumentNullException.ThrowIfNull(info);

        ViewId = info.ViewId;
        Icon = info.Icon;
        _requiredPermission = info.RequiredPermission;
        _titleResourceKey = info.TitleResourceKey;
        _fallbackTitle = info.Title;
        _permissionService = permissionService ?? throw new ArgumentNullException(nameof(permissionService));
        _languageService = languageService ?? throw new ArgumentNullException(nameof(languageService));
        _navigate = navigate ?? throw new ArgumentNullException(nameof(navigate));
        Title = ResolveTitle();
        RefreshPermission();
    }

    public string ViewId { get; }

    public string Icon { get; }

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateCommand))]
    private bool isAccessible;

    [ObservableProperty]
    private bool isSelected;

    public void RefreshPermission()
    {
        IsAccessible = string.IsNullOrWhiteSpace(_requiredPermission)
            || _permissionService.HasPermission(_requiredPermission);
    }

    public void RefreshTitle() => Title = ResolveTitle();

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void Navigate()
    {
        _navigate(ViewId);
    }

    private bool CanNavigate() => IsAccessible;

    private string ResolveTitle()
    {
        if (!string.IsNullOrWhiteSpace(_titleResourceKey))
        {
            var text = _languageService.GetText(_titleResourceKey);
            if (!string.Equals(text, _titleResourceKey, StringComparison.Ordinal))
            {
                return text;
            }
        }

        return string.IsNullOrWhiteSpace(_fallbackTitle)
            ? ViewId
            : _fallbackTitle;
    }
}
