using IIoT.Edge.Application.Features.Production.DataView;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.DataView;

public class DataViewModel : PresentationViewModelBase
{
    private readonly IDataViewService _dataViewService;
    private readonly string _viewId;
    private readonly string _viewTitle;

    public override string ViewId => _viewId;
    public override string ViewTitle => _viewTitle;

    private int _todayTotal;
    public int TodayTotal
    {
        get => _todayTotal;
        set { _todayTotal = value; OnPropertyChanged(); }
    }

    private int _todayOk;
    public int TodayOk
    {
        get => _todayOk;
        set { _todayOk = value; OnPropertyChanged(); }
    }

    private int _todayNg;
    public int TodayNg
    {
        get => _todayNg;
        set { _todayNg = value; OnPropertyChanged(); }
    }

    private string _todayYield = "0.00%";
    public string TodayYield
    {
        get => _todayYield;
        set { _todayYield = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ProductionRecordVm> Records { get; } = new();

    private DateTime _dateFrom = DateTime.Today;
    public DateTime DateFrom
    {
        get => _dateFrom;
        set { _dateFrom = value; OnPropertyChanged(); }
    }

    private DateTime _dateTo = DateTime.Today;
    public DateTime DateTo
    {
        get => _dateTo;
        set { _dateTo = value; OnPropertyChanged(); }
    }

    public ICommand QueryCommand { get; }
    public ICommand ExportCommand { get; }

    public DataViewModel(IDataViewService dataViewService)
        : this(dataViewService, "Production.DataView", "生产数据")
    {
    }

    protected DataViewModel(
        IDataViewService dataViewService,
        string viewId,
        string viewTitle)
    {
        _dataViewService = dataViewService;
        _viewId = viewId;
        _viewTitle = viewTitle;
        QueryCommand = new AsyncCommand(() => RunViewTaskAsync(QueryAsync, "生产数据查询失败"));
        ExportCommand = new BaseCommand(_ => { });
    }

    public override async Task OnActivatedAsync()
    {
        await RunViewTaskAsync(QueryAsync, "生产数据加载失败");
    }

    private async Task QueryAsync()
    {
        var snapshot = await _dataViewService.QueryAsync(DateFrom, DateTo);

        TodayTotal = snapshot.TodayTotal;
        TodayOk = snapshot.TodayOk;
        TodayNg = snapshot.TodayNg;
        TodayYield = snapshot.TodayYield;

        ReplaceItems(
            Records,
            snapshot.Records.Select(record => new ProductionRecordVm
            {
                Time = record.Time,
                BatchNo = record.BatchNo,
                Total = record.Total,
                OkCount = record.OkCount,
                NgCount = record.NgCount,
                Yield = record.Yield
            }));
    }
}

public class ProductionRecordVm
{
    public string Time { get; set; } = "";
    public string BatchNo { get; set; } = "";
    public int Total { get; set; }
    public int OkCount { get; set; }
    public int NgCount { get; set; }
    public string Yield { get; set; } = "";
}
