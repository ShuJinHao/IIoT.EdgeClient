using System.Collections.ObjectModel;
using System.Threading;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public interface IDiagnosticsViewModelCallback
{
    bool CanOperateDeadLetters { get; }

    Task RefreshAsync(CancellationToken cancellationToken);

    void RefreshPermissionState();

    void NotifyPropertyChanged(string propertyName);

    void SetStatus(string message);

    void SetError(string message);

    string GetText(string resourceKey, string fallback);

    string FormatText(string resourceKey, string fallback, params object[] arguments);
}

public interface IDiagnosticsViewModelCollaboratorFactory
{
    DiagnosticsViewModelCollaborators Create(DiagnosticsViewModelCollaboratorContext context);
}

public sealed record DiagnosticsViewModelCollaborators(
    IDiagnosticsTabController TabController,
    IDiagnosticsViewModelRefreshApplier RefreshApplier,
    IDiagnosticsDeadLetterCommandWorkflow DeadLetterWorkflow,
    IDiagnosticsPermissionObserver PermissionObserver);

public sealed class DiagnosticsViewModelCollaboratorContext
{
    internal DiagnosticsViewModelCollaboratorContext(
        IDiagnosticsViewModelCallback callback,
        ObservableCollection<DiagnosticsTabItemViewModel> tabs,
        DiagnosticsSummaryState summaryState,
        DiagnosticsCollectionTargets collections)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(summaryState);
        ArgumentNullException.ThrowIfNull(collections);

        Callback = callback;
        Tabs = tabs;
        SummaryState = summaryState;
        Collections = collections;
    }

    public IDiagnosticsViewModelCallback Callback { get; }

    public ObservableCollection<DiagnosticsTabItemViewModel> Tabs { get; }

    internal DiagnosticsSummaryState SummaryState { get; }

    internal DiagnosticsCollectionTargets Collections { get; }
}

internal sealed class DiagnosticsViewModelCollaboratorFactory(
    IAppLanguageService languageService,
    IDiagnosticsDeadLetterOperator deadLetterOperator,
    IDiagnosticsDeadLetterConfirmationService confirmationService,
    IClientPermissionService permissionService)
    : IDiagnosticsViewModelCollaboratorFactory
{
    public DiagnosticsViewModelCollaborators Create(DiagnosticsViewModelCollaboratorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new DiagnosticsViewModelCollaborators(
            new DiagnosticsTabController(context.Tabs, languageService, context.Callback),
            new DiagnosticsViewModelRefreshApplier(context.SummaryState, context.Collections, context.Callback),
            new DiagnosticsDeadLetterCommandWorkflow(context.Callback, deadLetterOperator, confirmationService),
            new DiagnosticsPermissionObserver(permissionService, context.Callback));
    }
}
