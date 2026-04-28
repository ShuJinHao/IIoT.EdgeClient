using System.Globalization;

namespace IIoT.Edge.UI.Shared.Localization;

/// <summary>
/// 界面语言下拉框选项。
/// </summary>
public sealed record LanguageOption(CultureInfo Culture, string DisplayName)
{
    public string Name => Culture.Name;
}
