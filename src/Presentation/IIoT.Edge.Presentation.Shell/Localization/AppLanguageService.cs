using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using IIoT.Edge.UI.Shared.Localization;
using WpfApplication = System.Windows.Application;

namespace IIoT.Edge.Presentation.Shell.Localization;

/// <summary>
/// 通过替换 Application 级 ResourceDictionary 实现 WPF 动态资源语言切换。
/// </summary>
public sealed class AppLanguageService : IAppLanguageService
{
    private const string DefaultCultureName = "zh-CN";
    private const string LanguageFileName = "language.json";
    private readonly string _storagePath;
    private CultureInfo _current;

    public AppLanguageService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IIoT.Edge",
            LanguageFileName))
    {
    }

    public AppLanguageService(string storagePath)
    {
        _storagePath = storagePath;
        _current = LoadPersistedCulture();
        SupportedLanguages =
        [
            new(CultureInfo.GetCultureInfo("zh-CN"), "中文"),
            new(CultureInfo.GetCultureInfo("en-US"), "English")
        ];
    }

    public CultureInfo Current => _current;

    public LanguageOption CurrentOption => SupportedLanguages.First(x => x.Culture.Name == _current.Name);

    public IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    public event EventHandler? LanguageChanged;

    public void Initialize() => ApplyCulture(_current, persist: false);

    public void Change(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var supported = SupportedLanguages.FirstOrDefault(x =>
            string.Equals(x.Culture.Name, culture.Name, StringComparison.OrdinalIgnoreCase));
        if (supported is null)
        {
            throw new InvalidOperationException($"不支持的界面语言：{culture.Name}。");
        }

        ApplyCulture(supported.Culture, persist: true);
    }

    public string GetString(string key, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        var value = WpfApplication.Current?.TryFindResource(key) as string;
        return string.IsNullOrWhiteSpace(value)
            ? (string.IsNullOrWhiteSpace(fallback) ? key : fallback)
            : value;
    }

    public string Format(string key, string fallback, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, GetString(key, fallback), args);

    private void ApplyCulture(CultureInfo culture, bool persist)
    {
        _current = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        ReplaceLanguageDictionaries(culture.Name);
        DataGridColumnLocalization.RefreshOpenWindows();

        if (persist)
        {
            SaveCulture(culture);
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ReplaceLanguageDictionaries(string cultureName)
    {
        var resources = WpfApplication.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var oldDictionaries = resources.MergedDictionaries
            .Where(IsLanguageDictionary)
            .ToArray();
        foreach (var dictionary in oldDictionaries)
        {
            resources.MergedDictionaries.Remove(dictionary);
        }

        foreach (var assemblyName in GetResourceAssemblyNames())
        {
            var dictionary = TryCreateLanguageDictionary(assemblyName, cultureName);
            if (dictionary is not null)
            {
                resources.MergedDictionaries.Add(dictionary);
            }
        }
    }

    private static bool IsLanguageDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        return source is not null
            && source.Contains("/Resources/Languages/", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetResourceAssemblyNames()
    {
        var names = new List<string>
        {
            "IIoT.Edge.Shell",
            "IIoT.Edge.Presentation.Shell",
            "IIoT.Edge.Presentation.Navigation",
            "IIoT.Edge.Presentation.Panels"
        };

        names.AddRange(AppDomain.CurrentDomain.GetAssemblies()
            .Select(x => x.GetName().Name)
            .Where(x => x is not null
                && x.StartsWith("IIoT.Edge.Module.", StringComparison.Ordinal)
                && !x.EndsWith(".Abstractions", StringComparison.Ordinal)
                && !x.EndsWith(".Contracts", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Select(x => x!));

        return names.Distinct(StringComparer.Ordinal);
    }

    private static ResourceDictionary? TryCreateLanguageDictionary(string assemblyName, string cultureName)
    {
        try
        {
            return new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/{assemblyName};component/Resources/Languages/{cultureName}.xaml",
                    UriKind.Absolute)
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (XamlParseException)
        {
            return null;
        }
    }

    private CultureInfo LoadPersistedCulture()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return CultureInfo.GetCultureInfo(DefaultCultureName);
            }

            var json = File.ReadAllText(_storagePath);
            var state = JsonSerializer.Deserialize<LanguageState>(json);
            var cultureName = state?.CultureName;
            if (string.IsNullOrWhiteSpace(cultureName))
            {
                return CultureInfo.GetCultureInfo(DefaultCultureName);
            }

            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch
        {
            return CultureInfo.GetCultureInfo(DefaultCultureName);
        }
    }

    private void SaveCulture(CultureInfo culture)
    {
        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                _storagePath,
                JsonSerializer.Serialize(new LanguageState(culture.Name)));
        }
        catch
        {
            // 语言偏好写入失败不能阻断主界面切换。
        }
    }

    private sealed record LanguageState(string CultureName);
}
