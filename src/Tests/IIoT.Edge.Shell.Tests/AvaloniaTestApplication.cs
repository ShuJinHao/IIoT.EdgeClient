using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(IIoT.Edge.Shell.Tests.ShellTestAvaloniaAppBuilder))]

namespace IIoT.Edge.Shell.Tests;

public sealed class ShellTestAvaloniaApplication : global::Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://IIoT.Edge.Shell.Tests"))
        {
            Source = new Uri("avares://IIoT.Edge.UI.Shared/Avalonia/Styles/EdgeTheme.axaml")
        });
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://IIoT.Edge.Shell.Tests"))
        {
            Source = new Uri("avares://IIoT.Edge.Presentation.Shell/Resources/Languages/zh-CN.axaml")
        });
    }
}

public static class ShellTestAvaloniaAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<ShellTestAvaloniaApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
