using IIoT.Edge.Host.Bootstrap;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.AvaloniaShell.Services;

internal static class AvaloniaShellStartupDependencyInjection
{
    public static IServiceCollection AddAvaloniaShellStartupServices(this IServiceCollection services)
    {
        services.AddSingleton<IShellConfigurationLoader, ShellConfigurationLoader>();
        services.AddSingleton<IShellRuntimePathResolver, ShellRuntimePathResolver>();
        services.AddSingleton<IAvaloniaShellBootstrapOptionsFactory, AvaloniaShellBootstrapOptionsFactory>();
        return services;
    }
}
