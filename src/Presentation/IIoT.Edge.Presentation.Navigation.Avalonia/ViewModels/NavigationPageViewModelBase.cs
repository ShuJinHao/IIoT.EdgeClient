using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public abstract class NavigationPageViewModelBase : AvaloniaViewModelBase
{
    private readonly IAvaloniaLanguageService _languageService;
    private readonly string _viewId;
    private readonly string _titleResourceKey;
    private readonly string _titleFallback;

    protected NavigationPageViewModelBase(
        IAvaloniaLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
    {
        _languageService = languageService;
        _viewId = viewId;
        _titleResourceKey = titleResourceKey;
        _titleFallback = titleFallback;
    }

    public override string ViewId => _viewId;

    public override string ViewTitle
        => _languageService.GetText(_titleResourceKey) is { } title && title != _titleResourceKey
            ? title
            : _titleFallback;
}
