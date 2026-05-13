using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.UI.Avalonia.Localization;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed partial class DataViewModel : NavigationPageViewModelBase
{
    public DataViewModel(
        IAvaloniaLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        Records =
        [
            new ProductionRecordRow(DateTime.Now.AddMinutes(-8).ToString("HH:mm:ss"), "B20260513001", 64, 63, 1, "98.44%"),
            new ProductionRecordRow(DateTime.Now.AddMinutes(-4).ToString("HH:mm:ss"), "B20260513002", 64, 64, 0, "100.00%")
        ];
    }

    public ObservableCollection<ProductionRecordRow> Records { get; }

    public int TodayTotal => Records.Sum(item => item.Total);

    public int TodayOk => Records.Sum(item => item.Ok);

    public int TodayNg => Records.Sum(item => item.Ng);

    [RelayCommand]
    private void Query()
    {
    }

    [RelayCommand]
    private void Export()
    {
    }
}

public sealed record ProductionRecordRow(string Time, string BatchNo, int Total, int Ok, int Ng, string Yield);
