using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(IIoT.Edge.UI.Shared.Tests.SharedUiTestAvaloniaAppBuilder))]

namespace IIoT.Edge.UI.Shared.Tests;

public sealed class SharedUiTestAvaloniaApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://IIoT.Edge.UI.Shared.Tests"))
        {
            Source = new Uri("avares://IIoT.Edge.UI.Shared/Avalonia/Styles/EdgeTheme.axaml")
        });
    }
}

public static class SharedUiTestAvaloniaAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<SharedUiTestAvaloniaApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
