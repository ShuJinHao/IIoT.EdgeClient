using System.Globalization;

namespace IIoT.Edge.UI.Shared.Localization;

/// <summary>
/// Shell 侧语言服务，负责切换 WPF 动态资源字典。
/// </summary>
public interface IAppLanguageService
{
    CultureInfo Current { get; }

    LanguageOption CurrentOption { get; }

    IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    event EventHandler? LanguageChanged;

    void Initialize();

    void Change(CultureInfo culture);

    string GetString(string key, string fallback = "");

    string Format(string key, string fallback, params object[] args);
}
