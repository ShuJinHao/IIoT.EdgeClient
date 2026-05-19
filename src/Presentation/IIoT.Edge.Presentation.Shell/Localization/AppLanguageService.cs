using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Styling;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Shell.Localization;

/// <summary>
/// 通过替换 Avalonia 应用级资源实现界面语言切换。
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

        var app = global::Avalonia.Application.Current;
        if (app?.TryGetResource(key, null, out var value) == true && value is string text && !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return string.IsNullOrWhiteSpace(fallback) ? key : fallback;
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

        if (persist)
        {
            SaveCulture(culture);
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ReplaceLanguageDictionaries(string cultureName)
    {
        var resources = global::Avalonia.Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var oldDictionaries = resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .Where(include => include.Source?.OriginalString.Contains("/Resources/Languages/", StringComparison.OrdinalIgnoreCase) == true)
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

    private static IEnumerable<string> GetResourceAssemblyNames()
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assemblyName in new[]
        {
            "IIoT.Edge.Shell",
            "IIoT.Edge.Presentation.Shell",
            "IIoT.Edge.Presentation.Navigation",
            "IIoT.Edge.Presentation.Panels"
        })
        {
            if (yielded.Add(assemblyName))
            {
                yield return assemblyName;
            }
        }

        foreach (var assemblyName in AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .Select(assembly => assembly.GetName().Name)
            .Where(name => !string.IsNullOrWhiteSpace(name)
                && name.StartsWith("IIoT.Edge.Module.", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (assemblyName is not null && yielded.Add(assemblyName))
            {
                yield return assemblyName;
            }
        }
    }

    private static ResourceInclude? TryCreateLanguageDictionary(string assemblyName, string cultureName)
    {
        try
        {
            var source = new Uri($"avares://{assemblyName}/Resources/Languages/{cultureName}.axaml");
            if (!AssetLoader.Exists(source))
            {
                return null;
            }

            return new ResourceInclude(source)
            {
                Source = source
            };
        }
        catch
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
