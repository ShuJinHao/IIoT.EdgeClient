using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Markup.Xaml;

namespace IIoT.Edge.Installer;

public partial class App : Application
{
    internal static InstallerOptions Options { get; set; } = new(null, false, false);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        InstallerLanguageResources.Apply(CultureInfo.CurrentUICulture);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new InstallerWindow(Options);
            desktop.MainWindow = window;
            window.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal static class InstallerLanguageResources
{
    private const string DefaultCultureName = "zh-CN";
    private const string EnglishCultureName = "en-US";
    private const string LanguageResourceMarker = "/Resources/Languages/";
    private static string InstallerAssemblyName
        => typeof(App).Assembly.GetName().Name
            ?? throw new InvalidOperationException("Installer assembly name could not be resolved.");

    public static void Apply(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var resources = app.Resources;
        if (resources is null)
        {
            resources = new ResourceDictionary();
            app.Resources = resources;
        }

        var oldDictionaries = resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .Where(include => include.Source?.OriginalString.Contains(
                LanguageResourceMarker,
                StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        foreach (var dictionary in oldDictionaries)
        {
            resources.MergedDictionaries.Remove(dictionary);
        }

        var source = BuildLanguageResourceUri(ResolveCultureName(culture));
        resources.MergedDictionaries.Add(new ResourceInclude(source)
        {
            Source = source
        });
    }

    internal static string ResolveCultureName(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? DefaultCultureName
            : EnglishCultureName;
    }

    internal static Uri BuildLanguageResourceUri(string cultureName)
        => new($"avares://{InstallerAssemblyName}/Resources/Languages/{cultureName}.axaml");
}
