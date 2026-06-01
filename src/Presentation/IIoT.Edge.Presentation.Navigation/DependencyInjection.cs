using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Features.Configuration;
using IIoT.Edge.Presentation.Navigation.Features.Dashboard;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.PlcTaskBindingView;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Features.Production.DataView;
using IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;
using IIoT.Edge.Presentation.Navigation.Features.Shell;
using IIoT.Edge.Presentation.Navigation.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Presentation.Navigation;

public static class DependencyInjection
{
    public static IServiceCollection AddNavigationPresentation(this IServiceCollection services)
    {
        services.AddSingleton<LocalizedSyncDiagnosticsText>();
        services.AddSingleton<NavigationRailViewModel>();
        services.AddSingleton<OverviewWorkspaceViewModel>();
        services.AddSingleton<ConfigurationWorkspaceViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ParamViewModel>();
        services.AddSingleton<IIoViewMappingBuilder, IoViewMappingBuilder>();
        services.AddSingleton<IIoViewSignalValueUpdater, IoViewSignalValueUpdater>();
        services.AddSingleton<IIoViewBufferBindingCoordinator, IoViewBufferBindingCoordinator>();
        services.AddSingleton<IIoViewInteractionWriter, IoViewInteractionWriter>();
        services.AddSingleton<IIoViewManualReadService, IoViewManualReadService>();
        services.AddSingleton<IoViewViewModel>();
        services.AddSingleton<IEditorValidator<NetworkDeviceVm>, NetworkDeviceValidator>();
        services.AddSingleton<IEditorValidator<SerialDeviceVm>, SerialDeviceValidator>();
        services.AddSingleton<IEditorValidator<IoMappingVm>, IoMappingValidator>();
        services.AddSingleton<IHardwareConfigValidationPresenter, HardwareConfigValidationPresenter>();
        services.AddSingleton<IHardwareConfigStandardSignalDraftService, HardwareConfigStandardSignalDraftService>();
        services.AddSingleton<IHardwareConfigMappingSaveBuilder, HardwareConfigMappingSaveBuilder>();
        services.AddSingleton<IHardwareConfigEditSession, HardwareConfigEditSession>();
        services.AddSingleton<IHardwareConfigDeviceSelectionCoordinator, HardwareConfigDeviceSelectionCoordinator>();
        services.AddSingleton<IHardwareConfigLoadSaveCoordinator, HardwareConfigLoadSaveCoordinator>();
        services.AddSingleton<HardwareConfigViewModel>();
        services.AddSingleton<IPlcTaskBindingConfirmationService, PlcTaskBindingConfirmationService>();
        services.AddSingleton<PlcTaskBindingViewModel>();
        services.AddSingleton<IEditorValidator<LocalRecipeParamEditModel>, LocalRecipeParamValidator>();
        services.AddSingleton<RecipeViewModel>();
        services.AddSingleton<CapacityViewModel>();
        services.AddSingleton<IMonitorViewModelSummaryFormatter, MonitorViewModelSummaryFormatter>();
        services.AddSingleton<IMonitorStateMachineTaskItemFactory, MonitorStateMachineTaskItemFactory>();
        services.AddSingleton<IMonitorViewModelCollaboratorFactory, MonitorViewModelCollaboratorFactory>();
        services.AddSingleton<MonitorViewModel>();
        services.AddSingleton<DataViewModel>();
        services.AddSingleton<IDiagnosticsModuleDisplayNameResolver, DiagnosticsModuleDisplayNameResolver>();
        services.AddSingleton<IDiagnosticsSummaryBuilder, DiagnosticsSummaryBuilder>();
        services.AddSingleton<IDiagnosticsRowsBuilder, DiagnosticsRowsBuilder>();
        services.AddSingleton<IDiagnosticsInitialSummaryFactory, DiagnosticsInitialSummaryFactory>();
        services.AddSingleton<IDiagnosticsRefreshCoordinator, DiagnosticsRefreshCoordinator>();
        services.AddSingleton<IDiagnosticsDeadLetterOperator, DiagnosticsDeadLetterOperator>();
        services.AddSingleton<IDiagnosticsDeadLetterConfirmationService, DiagnosticsDeadLetterConfirmationService>();
        services.AddSingleton<IDiagnosticsViewModelCollaboratorFactory, DiagnosticsViewModelCollaboratorFactory>();
        services.AddSingleton<DiagnosticsViewModel>();

        services.AddTransient<NavigationRailView>();
        services.AddTransient<NavigationHostView>();
        services.AddTransient<OverviewWorkspaceView>();
        services.AddTransient<ConfigurationWorkspaceView>();
        services.AddTransient<DashboardPreviewView>();
        services.AddTransient<DashboardView>();
        services.AddTransient<ParamViewPage>();
        services.AddTransient<IOViewPage>();
        services.AddTransient<HardwareConfigPage>();
        services.AddTransient<PlcTaskBindingPage>();
        services.AddTransient<RecipeViewPage>();
        services.AddTransient<CapacityViewPage>();
        services.AddTransient<MonitorViewPage>();
        services.AddTransient<DataViewPage>();
        services.AddTransient<DiagnosticsPage>();

        return services;
    }
}
