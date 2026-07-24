using System.Globalization;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.UI.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Launcher;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownRequested += (_, _) => DisposeServices();
            StartLauncher(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartLauncher(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            _serviceProvider = new ServiceCollection()
                .AddLauncherServices(AppDomain.CurrentDomain.BaseDirectory)
                .BuildServiceProvider();
            _serviceProvider.GetRequiredService<IAppLanguageService>().Initialize();
            _serviceProvider.GetRequiredService<ILauncherAccountCatalogInitializer>()
                .EnsureCatalogExists();
            _serviceProvider.GetRequiredService<IEdgeUpdateConfigInitializer>()
                .EnsureConfigExists();
            _serviceProvider.GetRequiredService<ILauncherPluginActivationReconciler>()
                .Reconcile();
            _serviceProvider.GetRequiredService<ILauncherDeviceBindingImporter>()
                .ApplyPendingBindings();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            DisposeServices();
            EnsureLanguageResources();
            ShowStartupError(
                desktop,
                CreateSafeStartupErrorMessage(ex));
        }
    }

    private void DisposeServices()
    {
        _serviceProvider?.Dispose();
        _serviceProvider = null;
    }

    private static void ShowStartupError(IClassicDesktopStyleApplicationLifetime desktop, string message)
    {
        var window = new LauncherStartupErrorWindow(
            ResourceText("Launcher_WindowTitle", LauncherStartupErrorWindow.FallbackWindowTitle),
            ResourceText("Launcher_Startup_ErrorTitle", LauncherStartupErrorWindow.FallbackTitle),
            message,
            ResourceText("Launcher_ToolTip_Close", LauncherStartupErrorWindow.FallbackCloseText));
        window.Closed += (_, _) => desktop.Shutdown(-1);
        desktop.MainWindow = window;
        window.Show();
    }

    private static void EnsureLanguageResources()
    {
        try
        {
            new LauncherLanguageService().Initialize();
        }
        catch
        {
            // 启动失败弹窗本身是最后兜底，资源加载失败时保持空文案，不再抛出二次异常。
        }
    }

    private static string ResourceText(string key, string fallback = "")
    {
        var app = global::Avalonia.Application.Current;
        return app?.TryGetResource(key, null, out var value) == true
            && value is string text
            && !string.IsNullOrWhiteSpace(text)
                ? text
                : fallback;
    }

    private static string ResourceFormat(string key, string fallback, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, ResourceText(key, fallback), args);

    internal static string CreateSafeStartupErrorMessage(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return ResourceFormat(
            "Launcher_Startup_ErrorFormat",
            "本地启动器初始化失败：{0}",
            exception.GetType().Name);
    }
}

internal sealed class LauncherLanguageService : IAppLanguageService
{
    private const string DefaultCultureName = "zh-CN";
    private readonly string _storagePath;
    private CultureInfo _current;

    public LauncherLanguageService()
        : this(EdgeClientProgramDataPaths.ResolveLauncherLanguagePath())
    {
    }

    public LauncherLanguageService(string storagePath)
    {
        _storagePath = storagePath;
        _current = LoadPersistedCulture();
        SupportedLanguages =
        [
            new(CultureInfo.GetCultureInfo("zh-CN"), "\u4e2d\u6587"),
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
            throw new InvalidOperationException($"Unsupported launcher UI language: {culture.Name}.");
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
        if (app?.TryGetResource(key, null, out var value) == true
            && value is string text
            && !string.IsNullOrWhiteSpace(text))
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

        ReplaceLanguageDictionary(culture.Name);

        if (persist)
        {
            SaveCulture(culture);
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ReplaceLanguageDictionary(string cultureName)
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

        var source = new Uri($"avares://IIoT.Edge.Launcher/Resources/Languages/{cultureName}.axaml");
        resources.MergedDictionaries.Add(new ResourceInclude(source)
        {
            Source = source
        });
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
            return string.IsNullOrWhiteSpace(state?.CultureName)
                ? CultureInfo.GetCultureInfo(DefaultCultureName)
                : CultureInfo.GetCultureInfo(state.CultureName);
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
            // 语言持久化失败不影响 Launcher 启动。
        }
    }

    private sealed record LanguageState(string CultureName);
}
