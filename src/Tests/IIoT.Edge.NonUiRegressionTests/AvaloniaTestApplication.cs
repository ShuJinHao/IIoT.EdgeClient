using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(IIoT.Edge.NonUiRegressionTests.TestAvaloniaAppBuilder))]

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class TestAvaloniaApplication : global::Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}

public static class TestAvaloniaAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestAvaloniaApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
