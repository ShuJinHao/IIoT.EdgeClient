using IIoT.Edge.Presentation.Navigation.Common.Crud;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Localization;

/// <summary>
/// 标准配置类页面 ViewModel 基类，统一处理页面标题和运行时语言切换。
/// </summary>
public abstract class LocalizedCrudPageViewModelBase : CrudPageViewModelBase
{
    private readonly IAppLanguageService _languageService;
    private readonly string _viewId;
    private readonly string _titleResourceKey;
    private readonly string _titleFallback;

    protected LocalizedCrudPageViewModelBase(
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
    {
        _languageService = languageService;
        _viewId = viewId;
        _titleResourceKey = titleResourceKey;
        _titleFallback = titleFallback;
        _languageService.LanguageChanged += (_, _) => RefreshLocalization();
    }

    public override string ViewId => _viewId;

    public override string ViewTitle => GetText(_titleResourceKey, _titleFallback);

    protected internal string GetText(string key, string fallback)
        => _languageService.GetString(key, fallback);

    protected internal string FormatText(string key, string fallback, params object[] args)
        => _languageService.Format(key, fallback, args);

    protected virtual void RefreshLocalization()
        => OnPropertyChanged(nameof(ViewTitle));
}
