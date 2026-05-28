using IIoT.Edge.Presentation.Shell.Localization;
using IIoT.Edge.UI.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Shell;

public static class DependencyInjection
{
    public static IServiceCollection AddShellPresentation(this IServiceCollection services)
    {
        services.AddSingleton<IAppLanguageService, AppLanguageService>();
        return services;
    }
}
