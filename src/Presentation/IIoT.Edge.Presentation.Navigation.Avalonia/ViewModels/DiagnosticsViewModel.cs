using IIoT.Edge.UI.Avalonia.Localization;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed class DiagnosticsViewModel : NavigationPageViewModelBase
{
    public DiagnosticsViewModel(
        IAvaloniaLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        Rows =
        [
            new DiagnosticsRow("Avalonia Shell", "Ready", languageService.GetText("Navigation_Diagnostics_RuntimeNotStarted")),
            new DiagnosticsRow("PLC Runtime", "Not started", languageService.GetText("Navigation_Diagnostics_RuntimeNotStarted")),
            new DiagnosticsRow("Cloud/MES", "Not started", languageService.GetText("Navigation_Diagnostics_RuntimeNotStarted"))
        ];
    }

    public ObservableCollection<DiagnosticsRow> Rows { get; }
}

public sealed record DiagnosticsRow(string DeviceName, string Status, string Message);
