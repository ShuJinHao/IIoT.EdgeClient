using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Module.DieCutting.Config;
using IIoT.Edge.Module.DieCutting.Production;
using IIoT.Edge.Presentation.Navigation.PluginSystem;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.PluginSystem;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.DieCutting.Presentation;

public sealed class DieCuttingDataViewModel : PresentationViewModelBase
{
    private const string EmptyValue = "—";
    private readonly DieCuttingModuleDefinition _definition;
    private readonly IDieCuttingProductionRecordStore _recordStore;
    private readonly IProductionContextStore _contextStore;
    private readonly IDeviceSelectionService _deviceSelectionService;
    private readonly IAppLanguageService _languageService;
    private readonly DispatcherTimer _timer;

    public DieCuttingDataViewModel(
        DieCuttingModuleDefinition definition,
        IDieCuttingProductionRecordStore recordStore,
        IProductionContextStore contextStore,
        IDeviceSelectionService deviceSelectionService,
        IAppLanguageService languageService,
        IOptions<DieCuttingModuleOptions> moduleOptions)
    {
        _definition = definition;
        _recordStore = recordStore;
        _contextStore = contextStore;
        _deviceSelectionService = deviceSelectionService;
        _languageService = languageService;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(NormalizeRefreshInterval(
                moduleOptions.Value.Presentation.DataViewRefreshIntervalMs))
        };
        _timer.Tick += (_, _) => RunViewTaskInBackground(RefreshAsync, "刷新模切生产数据失败");
        _deviceSelectionService.SelectionChanged += OnDeviceSelectionChanged;
        _languageService.LanguageChanged += (_, _) =>
        {
            RefreshLocalizedText();
            RunViewTaskInBackground(RefreshAsync, "刷新模切生产数据失败");
        };
    }

    public override string ViewId => StandardModuleViewIds.Create(_definition.ModuleId).DataView;

    public override string ViewTitle => $"{_definition.DisplayName}采样";

    public ObservableCollection<DieCuttingProductionRecordRow> Records { get; } = [];

    public bool IsRecordsEmpty => Records.Count == 0;

    public bool HasRecords => Records.Count > 0;

    public string EmptyTitle => GetText("DieCutting_Empty_Title", "暂无生产数据");

    public string EmptyMessage => IsSpecificDeviceSelected()
        ? GetText("DieCutting_Empty_SelectedDeviceProductionRecords", "当前 PLC 暂无生产数据。")
        : GetText("DieCutting_Empty_ProductionRecords", "当前没有真实模切生产记录。");

    public string DeviceNameHeader => GetText("DieCutting_Column_DeviceName", "设备号");

    public string BatchNoHeader => GetText("DieCutting_Column_BatchNo", "批次号");

    public string ClipNoHeader => GetText("DieCutting_Column_ClipNo", "弹夹号");

    public string QuantityHeader => GetText("DieCutting_Column_Quantity", "生产数量");

    public string StartTimeHeader => GetText("DieCutting_Column_StartTime", "开始时间");

    public string EndTimeHeader => GetText("DieCutting_Column_EndTime", "结束时间");

    public string PunchingSpeedHeader => GetText("DieCutting_Column_PunchingSpeed", "模切速度(PCS/min)");

    public string PlateLengthHeader => GetText("DieCutting_Column_PlateLength", "极片长度(mm)");

    public string PlateWidthHeader => GetText("DieCutting_Column_PlateWidth", "极片宽度(mm)");

    public string OperatorCodeHeader => GetText("DieCutting_Column_OperatorCode", "操作员工号");

    public string MoldCodeHeader => GetText("DieCutting_Column_MoldCode", "模具编号");

    public string CutterCodeHeader => GetText("DieCutting_Column_CutterCode", "切刀编号");

    public string RuntimeStatusText { get; private set; } = string.Empty;

    public string RuntimeSnapshotText { get; private set; } = string.Empty;

    public bool HasRuntimeSnapshot => !string.IsNullOrWhiteSpace(RuntimeSnapshotText);

    public override Task OnActivatedAsync()
    {
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }

        return RunViewTaskAsync(RefreshAsync, "加载模切生产数据失败");
    }

    public override Task OnDeactivatedAsync()
    {
        _timer.Stop();
        return Task.CompletedTask;
    }

    private async Task RefreshAsync()
    {
        var selectedDevice = _deviceSelectionService.SelectedDeviceKey;
        var rows = await _recordStore.QueryAsync(
            _definition.ModuleId,
            selectedDevice,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);
        var runtimeState = BuildRuntimeState(selectedDevice);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ReplaceItems(Records, rows.Select(ToRow));
            RuntimeStatusText = runtimeState.StatusText;
            RuntimeSnapshotText = runtimeState.SnapshotText;
            OnPropertyChanged(nameof(IsRecordsEmpty));
            OnPropertyChanged(nameof(HasRecords));
            OnPropertyChanged(nameof(RuntimeStatusText));
            OnPropertyChanged(nameof(RuntimeSnapshotText));
            OnPropertyChanged(nameof(HasRuntimeSnapshot));
            SetStatus(rows.Count == 0
                ? EmptyMessage
                : string.Format(CultureInfo.CurrentCulture, "共 {0} 条模切生产记录。", rows.Count));
        });
    }

    private void OnDeviceSelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(EmptyMessage));
        RunViewTaskInBackground(RefreshAsync, "刷新模切生产数据失败");
    }

    private void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));
        OnPropertyChanged(nameof(DeviceNameHeader));
        OnPropertyChanged(nameof(BatchNoHeader));
        OnPropertyChanged(nameof(ClipNoHeader));
        OnPropertyChanged(nameof(QuantityHeader));
        OnPropertyChanged(nameof(StartTimeHeader));
        OnPropertyChanged(nameof(EndTimeHeader));
        OnPropertyChanged(nameof(PunchingSpeedHeader));
        OnPropertyChanged(nameof(PlateLengthHeader));
        OnPropertyChanged(nameof(PlateWidthHeader));
        OnPropertyChanged(nameof(OperatorCodeHeader));
        OnPropertyChanged(nameof(MoldCodeHeader));
        OnPropertyChanged(nameof(CutterCodeHeader));
        OnPropertyChanged(nameof(RuntimeStatusText));
        OnPropertyChanged(nameof(RuntimeSnapshotText));
    }

    private string GetText(string key, string fallback)
        => _languageService.GetString(key, fallback);

    private bool IsSpecificDeviceSelected()
        => !string.IsNullOrWhiteSpace(_deviceSelectionService.SelectedDeviceKey)
           && !string.Equals(
               _deviceSelectionService.SelectedDeviceKey,
               IDeviceSelectionService.AllFilterKey,
               StringComparison.OrdinalIgnoreCase);

    private RuntimeState BuildRuntimeState(string? selectedDevice)
    {
        if (string.IsNullOrWhiteSpace(selectedDevice)
            || string.Equals(
                selectedDevice,
                IDeviceSelectionService.AllFilterKey,
                StringComparison.OrdinalIgnoreCase))
        {
            return new RuntimeState(
                GetText("DieCutting_Runtime_AllStatus", "全部/汇总模式显示本地生产记录；请选择右侧 PLC 查看实时采集状态。"),
                string.Empty);
        }

        var context = _contextStore
            .GetAll()
            .OfType<DieCuttingContext>()
            .FirstOrDefault(x => string.Equals(x.DeviceName, selectedDevice, StringComparison.OrdinalIgnoreCase));
        if (context is null)
        {
            return new RuntimeState(
                string.Format(
                    CultureInfo.CurrentCulture,
                    GetText("DieCutting_Runtime_NoContext", "当前 PLC“{0}”暂无模切采样运行上下文，请检查任务绑定是否已应用。"),
                    selectedDevice),
                string.Empty);
        }

        var lastAt = context.LastRealtimeAt.HasValue
            ? FormatTime(context.LastRealtimeAt.Value)
            : EmptyValue;
        var result = FormatText(context.LastRealtimeResult);
        var statusText = string.Format(
            CultureInfo.CurrentCulture,
            GetText("DieCutting_Runtime_LastResult", "最近处理：{0}，结果：{1}"),
            lastAt,
            result);

        var snapshot = context.LastRealtimeSnapshot;
        if (snapshot is null)
        {
            return new RuntimeState(
                statusText,
                GetText("DieCutting_Runtime_NoSnapshot", "当前还没有模切采样快照。"));
        }

        var snapshotText = string.Format(
            CultureInfo.CurrentCulture,
            GetText("DieCutting_Runtime_Snapshot", "批次={0}，产量={1}，冲切速度={2}，放卷长度={3}，弹夹={4}"),
            FormatText(snapshot.PunchingLotNumber),
            snapshot.PunchingQuantity.ToString(CultureInfo.InvariantCulture),
            snapshot.PunchingSpeed.ToString("0.#####", CultureInfo.InvariantCulture),
            snapshot.UnwindingLength.ToString(CultureInfo.InvariantCulture),
            FormatText(snapshot.ClipNo));
        return new RuntimeState(statusText, snapshotText);
    }

    private static DieCuttingProductionRecordRow ToRow(DieCuttingProductionRecord record)
        => new(
            FormatText(record.DeviceName),
            FormatText(record.BatchNo),
            FormatText(record.ClipNo),
            record.Quantity.ToString(CultureInfo.InvariantCulture),
            FormatTime(record.WindowStartAt),
            FormatTime(record.WindowCompleteAt),
            record.PunchingSpeed.ToString("0.#####", CultureInfo.InvariantCulture),
            FormatNullableNumber(record.PlateLengthMm),
            FormatNullableNumber(record.PlateWidthMm),
            FormatText(record.OperatorCode),
            FormatText(record.MoldCode),
            FormatText(record.CutterCode));

    private static string FormatText(string? value)
        => string.IsNullOrWhiteSpace(value) ? EmptyValue : value.Trim();

    private static string FormatTime(DateTime value)
        => value == default ? EmptyValue : value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    private static string FormatNullableNumber(decimal? value)
        => value.HasValue ? value.Value.ToString("0.#####", CultureInfo.InvariantCulture) : EmptyValue;

    private static int NormalizeRefreshInterval(int configured)
        => Math.Max(500, configured <= 0 ? 1000 : configured);

    private sealed record RuntimeState(string StatusText, string SnapshotText);
}

public sealed record DieCuttingProductionRecordRow(
    string DeviceName,
    string BatchNo,
    string ClipNo,
    string Quantity,
    string StartTime,
    string EndTime,
    string PunchingSpeed,
    string PlateLength,
    string PlateWidth,
    string OperatorCode,
    string MoldCode,
    string CutterCode);
