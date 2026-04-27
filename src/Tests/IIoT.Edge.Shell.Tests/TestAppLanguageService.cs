using System.Globalization;
using IIoT.Edge.Presentation.Shell.Localization;

namespace IIoT.Edge.Shell.Tests;

internal sealed class TestAppLanguageService : IAppLanguageService
{
    public CultureInfo Current { get; private set; } = CultureInfo.GetCultureInfo("zh-CN");

    public LanguageOption CurrentOption => SupportedLanguages.First(x => x.Culture.Name == Current.Name);

    public IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
    [
        new(CultureInfo.GetCultureInfo("zh-CN"), "中文"),
        new(CultureInfo.GetCultureInfo("en-US"), "English")
    ];

    public event EventHandler? LanguageChanged;

    public void Initialize()
    {
    }

    public void Change(CultureInfo culture)
    {
        Current = culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetString(string key, string fallback = "") => fallback;

    public string Format(string key, string fallback, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, fallback, args);
}
