using System.Globalization;
using System.Text.Json;
using System.Threading;

namespace IIoT.Edge.UI.Avalonia.Localization;

public sealed class AvaloniaResourceLanguageService : IAvaloniaLanguageService
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _resources;
    private readonly string _defaultCulture;
    private readonly string _toggleResourceKey;
    private readonly string _storagePath;

    public AvaloniaResourceLanguageService(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources,
        string defaultCulture = "zh-CN",
        string toggleResourceKey = "Shell_Action_Language",
        string? storagePath = null)
    {
        _resources = resources;
        _defaultCulture = defaultCulture;
        _toggleResourceKey = toggleResourceKey;
        _storagePath = string.IsNullOrWhiteSpace(storagePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IIoT.Edge",
                "language.json")
            : storagePath;
        CultureName = LoadPersistedCulture(defaultCulture);
    }

    public string CultureName { get; private set; }

    public string ToggleLabel => GetText(_toggleResourceKey);

    public event EventHandler? LanguageChanged;

    public string GetText(string key)
    {
        return _resources.TryGetValue(CultureName, out var current) && current.TryGetValue(key, out var value)
            ? value
            : key;
    }

    public void Apply(string cultureName)
    {
        var nextCulture = ResolveCulture(cultureName);
        CultureName = nextCulture;
        ApplyThreadCulture(nextCulture);

        if (global::Avalonia.Application.Current is { } application
            && _resources.TryGetValue(nextCulture, out var resourceValues))
        {
            foreach (var pair in resourceValues)
            {
                application.Resources[pair.Key] = pair.Value;
            }
        }

        SaveCulture(nextCulture);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle()
    {
        Apply(CultureName.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN");
    }

    private string ResolveCulture(string cultureName)
    {
        if (_resources.ContainsKey(cultureName))
        {
            return cultureName;
        }

        if (_resources.ContainsKey(_defaultCulture))
        {
            return _defaultCulture;
        }

        return _resources.Keys.FirstOrDefault() ?? cultureName;
    }

    private string LoadPersistedCulture(string fallback)
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                return ResolveCulture(fallback);
            }

            var state = JsonSerializer.Deserialize<LanguageState>(File.ReadAllText(_storagePath));
            return string.IsNullOrWhiteSpace(state?.CultureName)
                ? ResolveCulture(fallback)
                : ResolveCulture(state.CultureName);
        }
        catch
        {
            return ResolveCulture(fallback);
        }
    }

    private void SaveCulture(string cultureName)
    {
        try
        {
            var directory = Path.GetDirectoryName(_storagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_storagePath, JsonSerializer.Serialize(new LanguageState(cultureName)));
        }
        catch
        {
            // 语言偏好写入失败不能阻断主界面切换。
        }
    }

    private static void ApplyThreadCulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    private sealed record LanguageState(string CultureName);
}
