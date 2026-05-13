using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

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
}
