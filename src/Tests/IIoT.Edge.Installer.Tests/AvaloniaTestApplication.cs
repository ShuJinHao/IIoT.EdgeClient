using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(IIoT.Edge.Installer.Tests.InstallerTestAvaloniaAppBuilder))]

namespace IIoT.Edge.Installer.Tests;

public sealed class InstallerTestAvaloniaApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://IIoT.Edge.Installer.Tests"))
        {
            Source = new Uri("avares://IIoT.Edge.UI.Shared/Avalonia/Styles/EdgeTheme.axaml")
        });

        var languageSource = InstallerLanguageResources.BuildLanguageResourceUri("zh-CN");
        Resources.MergedDictionaries.Add(new ResourceInclude(languageSource)
        {
            Source = languageSource
        });
    }
}

public static class InstallerTestAvaloniaAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<InstallerTestAvaloniaApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
