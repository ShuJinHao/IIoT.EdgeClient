using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Avalonia.Localization;
using IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IIoT.Edge.Presentation.Navigation.Avalonia;

public static class DependencyInjection
{
    public static IServiceCollection AddNavigationAvaloniaPresentation(this IServiceCollection services)
    {
        services.AddSingleton<IAvaloniaResourceContributor, NavigationAvaloniaZhCnResources>();
        services.AddSingleton<IAvaloniaResourceContributor, NavigationAvaloniaEnUsResources>();
        services.AddSingleton<IIoViewWriteGateAuditStore, IoViewWriteGateAuditStore>();
        services.AddSingleton<IIoViewSafeInteractionPort, RuntimeBufferIoViewSafeInteractionPort>();
        services.TryAddSingleton<IAvaloniaDiagnosticsDeadLetterConfirmationService, AvaloniaDiagnosticsDeadLetterConfirmationService>();
        services.TryAddSingleton<IAvaloniaDiagnosticsDeadLetterOperator>(sp =>
            new AvaloniaDiagnosticsDeadLetterOperator(sp.GetService<IIoT.Edge.Application.Abstractions.DataPipeline.IDeadLetterMaintenanceService>()));
        return services;
    }
}
