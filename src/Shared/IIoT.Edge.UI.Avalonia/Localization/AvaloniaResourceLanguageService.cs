namespace IIoT.Edge.UI.Avalonia.Localization;

public sealed class AvaloniaResourceLanguageService : IAvaloniaLanguageService
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _resources;
    private readonly string _defaultCulture;
    private readonly string _toggleResourceKey;

    public AvaloniaResourceLanguageService(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources,
        string defaultCulture = "zh-CN",
        string toggleResourceKey = "Shell_Action_Language")
    {
        _resources = resources;
        _defaultCulture = defaultCulture;
        _toggleResourceKey = toggleResourceKey;
        CultureName = defaultCulture;
    }

    public string CultureName { get; private set; }

    public string ToggleLabel => GetText(_toggleResourceKey);

    public string GetText(string key)
    {
        return _resources.TryGetValue(CultureName, out var current) && current.TryGetValue(key, out var value)
            ? value
            : key;
    }

    public void Apply(string cultureName)
    {
        var nextCulture = _resources.ContainsKey(cultureName) ? cultureName : _defaultCulture;
        CultureName = nextCulture;

        if (global::Avalonia.Application.Current is not { } application)
        {
            return;
        }

        foreach (var pair in _resources[nextCulture])
        {
            application.Resources[pair.Key] = pair.Value;
        }
    }

    public void Toggle()
    {
        Apply(CultureName.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN");
    }
}
