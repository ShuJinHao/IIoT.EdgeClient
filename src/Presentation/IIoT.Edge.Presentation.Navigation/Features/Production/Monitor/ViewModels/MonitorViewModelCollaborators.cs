using System.Collections.ObjectModel;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

public interface IMonitorViewModelCallback
{
    void NotifyPropertyChanged(string propertyName);
}

public interface IMonitorViewModelCollaboratorFactory
{
    MonitorViewModelCollaborators Create(MonitorViewModelCollaboratorContext context);
}

public sealed record MonitorViewModelCollaborators(
    IMonitorViewModelTabController TabController,
    IMonitorViewModelSummaryFormatter SummaryFormatter,
    IMonitorStateMachineTaskItemFactory StateMachineTaskItemFactory);

public sealed class MonitorViewModelCollaboratorContext
{
    public MonitorViewModelCollaboratorContext(
        IMonitorViewModelCallback callback,
        ObservableCollection<MonitorTabItemViewModel> tabs)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(tabs);

        Callback = callback;
        Tabs = tabs;
    }

    public IMonitorViewModelCallback Callback { get; }

    public ObservableCollection<MonitorTabItemViewModel> Tabs { get; }
}

internal sealed class MonitorViewModelCollaboratorFactory(
    IAppLanguageService languageService,
    IMonitorViewModelSummaryFormatter summaryFormatter,
    IMonitorStateMachineTaskItemFactory stateMachineTaskItemFactory)
    : IMonitorViewModelCollaboratorFactory
{
    public MonitorViewModelCollaborators Create(MonitorViewModelCollaboratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new MonitorViewModelCollaborators(
            new MonitorViewModelTabController(context.Tabs, languageService, context.Callback),
            summaryFormatter,
            stateMachineTaskItemFactory);
    }
}
