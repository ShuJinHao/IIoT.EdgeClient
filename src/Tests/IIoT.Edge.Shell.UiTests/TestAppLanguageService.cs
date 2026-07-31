using System.Globalization;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Shell.UiTests;

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

    public string GetString(string key, string fallback = "")
        => (key, Current.Name) switch
        {
            ("Panels_Filter_AllOrSummary", "en-US") => "All / Summary",
            ("Panels_Filter_AllOrSummary", _) => "全部/汇总",
            ("Navigation_PlcTaskBinding_EmptyMagazineCode", "en-US") => "Empty code",
            ("Navigation_PlcTaskBinding_RecoveryState_AwaitingConfirmation", "en-US")
                => "Waiting for local confirmation",
            ("Navigation_PlcTaskBinding_RecoveryAction_AuditTerminate", "en-US")
                => "Audit terminate",
            _ => fallback
        };

    public string Format(string key, string fallback, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, fallback, args);
}
