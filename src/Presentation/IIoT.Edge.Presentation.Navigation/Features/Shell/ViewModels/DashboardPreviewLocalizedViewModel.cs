using IIoT.Edge.UI.Shared.Avalonia.Controls;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

/// <summary>
/// Dashboard 预览页本地化 ViewModel 基类，统一语言服务订阅、释放和格式化文本方法。
/// </summary>
internal abstract class DashboardPreviewLocalizedViewModel : BaseNotifyPropertyChanged, IDisposable
{
    private bool _disposed;

    protected DashboardPreviewLocalizedViewModel(IAppLanguageService languageService)
    {
        LanguageService = languageService;
        LanguageService.LanguageChanged += OnLanguageChangedCore;
    }

    protected IAppLanguageService LanguageService { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        LanguageService.LanguageChanged -= OnLanguageChangedCore;
        DisposeCore();
        _disposed = true;
    }

    protected abstract void OnLanguageChanged();

    protected virtual void DisposeCore()
    {
    }

    protected string GetText(string key, string fallback)
        => LanguageService.GetString(key, fallback);

    protected string FormatText(string key, string fallback, params object[] args)
        => string.Format(GetText(key, fallback), args);

    protected string FormatCount(int count)
        => FormatText("Navigation_DashboardPreview_CountFormat", "{0} 条", count);

    protected string ResolveUploadHealthStatusText(EdgeVisualStatus status, bool isDisabled)
    {
        if (isDisabled)
        {
            return GetText("Navigation_DashboardPreview_UploadDisabled", "上传未启用");
        }

        return ResolveUploadHealthSegmentLabel(status);
    }

    protected string ResolveUploadHealthSegmentLabel(EdgeVisualStatus status)
        => status switch
        {
            EdgeVisualStatus.Running => GetText("Navigation_DashboardPreview_UploadHealthy", "正常"),
            EdgeVisualStatus.Error => GetText("Navigation_DashboardPreview_UploadFailure", "失败"),
            _ => GetText("Navigation_DashboardPreview_NoUploadEvent", "无上传事件")
        };

    protected IReadOnlyList<EdgeSummaryItem> BuildUploadHealthSummaryItems(
        string lastSuccessText,
        string lastFailureText,
        string deadLetterText) =>
    [
        new()
        {
            Label = GetText("Navigation_DashboardPreview_LastUploadSuccess", "最近成功"),
            Value = lastSuccessText
        },
        new()
        {
            Label = GetText("Navigation_DashboardPreview_LastUploadFailure", "最近失败"),
            Value = lastFailureText
        },
        new()
        {
            Label = GetText("Navigation_DashboardPreview_DeadLetters", "死信"),
            Value = deadLetterText
        }
    ];

    private void OnLanguageChangedCore(object? sender, EventArgs e)
        => OnLanguageChanged();
}
