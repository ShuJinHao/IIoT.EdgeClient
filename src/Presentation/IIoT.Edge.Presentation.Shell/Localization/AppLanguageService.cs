using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Shell.Localization;

/// <summary>
/// 通过替换 Avalonia 应用级资源实现界面语言切换。
/// </summary>
public sealed class AppLanguageService : IAppLanguageService
{
    private const string DefaultCultureName = "zh-CN";
    private static readonly HashSet<string> RequiredResourceAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IIoT.Edge.Shell",
        "IIoT.Edge.Presentation.Shell",
        "IIoT.Edge.Presentation.Navigation",
        "IIoT.Edge.Presentation.Panels"
    };

    private readonly string _storagePath;
    private readonly List<IResourceProvider> _loadedLanguageDictionaries = [];
    private CultureInfo _current;

    public AppLanguageService()
        : this(EdgeClientProgramDataPaths.ResolveLauncherLanguagePath())
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

    private void ReplaceLanguageDictionaries(string cultureName)
    {
        var application = global::Avalonia.Application.Current;
        if (application is null)
        {
            return;
        }

        var resources = application.Resources;
        if (resources is null)
        {
            resources = new ResourceDictionary();
            application.Resources = resources;
        }

        var oldDictionaries = resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .Where(include => include.Source?.OriginalString.Contains("/Resources/Languages/", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        foreach (var dictionary in oldDictionaries)
        {
            resources.MergedDictionaries.Remove(dictionary);
        }

        foreach (var dictionary in _loadedLanguageDictionaries)
        {
            resources.MergedDictionaries.Remove(dictionary);
        }
        _loadedLanguageDictionaries.Clear();

        foreach (var assembly in GetResourceAssemblies())
        {
            var dictionary = TryLoadLanguageDictionary(assembly, cultureName);
            if (dictionary is not null)
            {
                resources.MergedDictionaries.Add(dictionary);
                _loadedLanguageDictionaries.Add(dictionary);
                continue;
            }

            var assemblyName = assembly.GetName().Name;
            if (assemblyName is not null && RequiredResourceAssemblyNames.Contains(assemblyName))
            {
                throw new InvalidOperationException(
                    $"缺少必需的界面语言资源：{assemblyName}/{cultureName}。");
            }
        }
    }

    private static IEnumerable<Assembly> GetResourceAssemblies()
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static assembly => !assembly.IsDynamic)
            .ToArray();

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
                yield return loadedAssemblies.FirstOrDefault(assembly =>
                           string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                       ?? Assembly.Load(new AssemblyName(assemblyName));
            }
        }

        foreach (var assembly in loadedAssemblies
            .Where(assembly =>
            {
                var name = assembly.GetName().Name;
                return !string.IsNullOrWhiteSpace(name)
                       && name.StartsWith("IIoT.Edge.Module.", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(assembly => assembly.GetName().Name, StringComparer.OrdinalIgnoreCase))
        {
            var assemblyName = assembly.GetName().Name;
            if (assemblyName is not null && yielded.Add(assemblyName))
            {
                yield return assembly;
            }
        }
    }

    internal static IResourceProvider? TryLoadLanguageDictionary(Assembly assembly, string cultureName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(cultureName);

        var assemblyName = assembly.GetName().Name
            ?? throw new InvalidOperationException("界面资源程序集缺少有效名称。");
        var source = new Uri($"avares://{assemblyName}/Resources/Languages/{cultureName}.axaml");

        try
        {
            var loaderType = assembly.GetType("CompiledAvaloniaXaml.!XamlLoader", throwOnError: false);
            var tryLoad = loaderType?.GetMethod(
                "TryLoad",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(string)],
                modifiers: null);
            if (tryLoad is null)
            {
                return null;
            }

            var loaded = tryLoad.Invoke(null, [source.ToString()]);
            return loaded switch
            {
                null => null,
                IResourceProvider resourceProvider => resourceProvider,
                _ => throw new InvalidOperationException(
                    $"界面语言资源返回了不受支持的类型：{loaded.GetType().FullName}。")
            };
        }
        catch (TargetInvocationException ex)
        {
            throw new InvalidOperationException(
                $"无法加载界面语言资源：{assemblyName}/{cultureName}。",
                ex.InnerException ?? ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"无法加载界面语言资源：{assemblyName}/{cultureName}。",
                ex);
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
