using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using IIoT.Edge.Launcher;

[assembly: AvaloniaTestApplication(typeof(IIoT.Edge.Launcher.UiTests.LauncherTestAvaloniaAppBuilder))]

namespace IIoT.Edge.Launcher.UiTests;

public sealed class LauncherTestAvaloniaApplication : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://IIoT.Edge.Launcher.UiTests"))
        {
            Source = new Uri("avares://IIoT.Edge.UI.Shared/Avalonia/Styles/EdgeTheme.axaml")
        });

        new LauncherLanguageService(Path.Combine(
            Path.GetTempPath(),
            $"iiot-launcher-test-language-{Guid.NewGuid():N}.json")).Initialize();
    }
}

public static class LauncherTestAvaloniaAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<LauncherTestAvaloniaApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
