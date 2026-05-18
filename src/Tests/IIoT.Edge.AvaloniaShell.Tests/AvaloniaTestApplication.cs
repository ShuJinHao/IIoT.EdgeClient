using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using DialogHostAvalonia;
using Dock.Avalonia.Themes.Fluent;
using Material.Icons.Avalonia;

[assembly: AvaloniaTestApplication(typeof(IIoT.Edge.AvaloniaShell.Tests.AvaloniaTestApplication))]

namespace IIoT.Edge.AvaloniaShell.Tests;

public static class AvaloniaTestApplication
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .UseSkia();
    }
}

public sealed class TestApplication : global::Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new MaterialIconStyles(null));
        Styles.Add(Include("avares://IIoT.Edge.UI.Avalonia/Themes/AppTypography.axaml"));
        Styles.Add(Include("avares://IIoT.Edge.UI.Avalonia/Themes/IndustrialTheme.axaml"));
        Styles.Add(Include("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"));
        Styles.Add(new DockFluentTheme());
        Styles.Add(new DialogHostStyles());
    }

    private static StyleInclude Include(string source)
    {
        var uri = new Uri(source);
        return new StyleInclude(uri)
        {
            Source = uri
        };
    }
}
