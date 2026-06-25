using IIoT.Edge.Application.Features.Production.DataView;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.DataView;

public class DataViewModel : NavigationViewModelBase
{
    private readonly IProductionDataQueryFacade _productionDataQueryFacade;
    private readonly IDeviceSelectionService _deviceSelectionService;

    public ObservableCollection<ProductionRecordVm> Records { get; } = new();
    public bool HasRecords => Records.Count > 0;
    public bool IsRecordsEmpty => Records.Count == 0;

    public ICommand QueryCommand { get; }

    public DataViewModel(
        IProductionDataQueryFacade productionDataQueryFacade,
        IAppLanguageService languageService,
        IDeviceSelectionService deviceSelectionService)
        : this(
            productionDataQueryFacade,
            languageService,
            deviceSelectionService,
            "Production.DataView",
            "Navigation_Title_Data",
            "生产数据")
    {
    }

    public DataViewModel(
        IProductionDataQueryFacade productionDataQueryFacade,
        IAppLanguageService languageService,
        IDeviceSelectionService deviceSelectionService,
        string viewId,
        string titleResourceKey,
        string titleFallback)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _productionDataQueryFacade = productionDataQueryFacade;
        _deviceSelectionService = deviceSelectionService;
        _deviceSelectionService.SelectionChanged += OnDeviceSelectionChanged;
        QueryCommand = new AsyncCommand(() => RunViewTaskAsync(
            QueryAsync,
            GetText("Navigation_Data_QueryFailed", "生产数据查询失败。")));
    }

    public override async Task OnActivatedAsync()
    {
        await RunViewTaskAsync(
            QueryAsync,
            GetText("Navigation_Data_LoadFailed", "生产数据加载失败。"));
    }

    private async Task QueryAsync()
    {
        var snapshot = await _productionDataQueryFacade.QueryAsync(_deviceSelectionService.SelectedDeviceKey);

        ReplaceItems(
            Records,
            snapshot.Records.Select(record => new ProductionRecordVm
            {
                DeviceName = record.DeviceName,
                Time = record.Time,
                BatchNo = record.BatchNo,
                Total = record.Total,
                OkCount = record.OkCount,
                NgCount = record.NgCount,
                Yield = record.Yield
            }));
        OnPropertyChanged(nameof(HasRecords));
        OnPropertyChanged(nameof(IsRecordsEmpty));
    }

    private void OnDeviceSelectionChanged(object? sender, EventArgs e)
        => RunViewTaskInBackground(
            QueryAsync,
            GetText("Navigation_Data_LoadFailed", "生产数据加载失败。"));
}

public class ProductionRecordVm
{
    public string DeviceName { get; set; } = "";
    public string Time { get; set; } = "";
    public string BatchNo { get; set; } = "";
    public int Total { get; set; }
    public int OkCount { get; set; }
    public int NgCount { get; set; }
    public string Yield { get; set; } = "";
}
