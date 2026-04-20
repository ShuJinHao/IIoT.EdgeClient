using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.DryRun.Constants;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.Module.DryRun.Presentation.ViewModels;

public sealed class DryRunDashboardViewModel : PresentationViewModelBase
{
    private readonly IStationRuntimeRegistry _runtimeRegistry;
    private readonly IProcessIntegrationRegistry _integrationRegistry;
    private readonly ICellDataRegistry _cellDataRegistry;

    public override string ViewId => DryRunViewIds.Dashboard;

    public override string ViewTitle => "DryRun Dashboard";

    public string ModuleId => DryRunModuleConstants.ModuleId;

    private string _moduleStatus = "DryRun module is ready for template validation.";
    public string ModuleStatus
    {
        get => _moduleStatus;
        private set
        {
            _moduleStatus = value;
            OnPropertyChanged();
        }
    }

    private string _registrationStatus = "Checking DryRun registrations...";
    public string RegistrationStatus
    {
        get => _registrationStatus;
        private set
        {
            _registrationStatus = value;
            OnPropertyChanged();
        }
    }

    public DryRunDashboardViewModel(
        IStationRuntimeRegistry runtimeRegistry,
        IProcessIntegrationRegistry integrationRegistry,
        ICellDataRegistry cellDataRegistry)
    {
        _runtimeRegistry = runtimeRegistry;
        _integrationRegistry = integrationRegistry;
        _cellDataRegistry = cellDataRegistry;
    }

    public override Task OnActivatedAsync()
    {
        var runtimeRegistered = _runtimeRegistry.HasFactory(DryRunModuleConstants.ModuleId);
        var uploaderRegistered = _integrationRegistry.HasCloudUploader(DryRunModuleConstants.ProcessType);
        var cellDataRegistered = _cellDataRegistry.IsRegistered(DryRunModuleConstants.ProcessType);

        RegistrationStatus =
            $"CellData:{(cellDataRegistered ? "Yes" : "No")}, Runtime:{(runtimeRegistered ? "Yes" : "No")}, CloudUploader:{(uploaderRegistered ? "Yes" : "No")}.";
        SetStatus("DryRun is an internal scaffold module for discovery, contract tests, and package validation.");
        return Task.CompletedTask;
    }
}
