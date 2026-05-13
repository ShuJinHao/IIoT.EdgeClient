using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.UI.Avalonia.Localization;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed partial class CapacityViewModel : NavigationPageViewModelBase
{
    public CapacityViewModel(
        IAvaloniaLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        Records =
        [
            new CapacityRecordRow("05-10", 860, 852, 8, "99.07%"),
            new CapacityRecordRow("05-11", 910, 904, 6, "99.34%"),
            new CapacityRecordRow("05-12", 887, 878, 9, "98.99%")
        ];
    }

    public ObservableCollection<CapacityRecordRow> Records { get; }

    public int PeriodTotal => Records.Sum(item => item.Total);

    public int PeriodOk => Records.Sum(item => item.Ok);

    public int PeriodNg => Records.Sum(item => item.Ng);

    [RelayCommand]
    private void Query()
    {
    }
}

public sealed record CapacityRecordRow(string Date, int Total, int Ok, int Ng, string Yield);
