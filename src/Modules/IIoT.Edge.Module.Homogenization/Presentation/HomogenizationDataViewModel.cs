using System.Collections.ObjectModel;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Mvvm;
using IIoT.Edge.UI.Avalonia.Services;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Presentation;

public sealed class HomogenizationDataViewModel : AvaloniaViewModelBase
{
    private readonly IProductionContextStore _contextStore;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IAvaloniaDispatcherService _dispatcher;
    private readonly IAvaloniaTimer _timer;

    public HomogenizationDataViewModel(
        IProductionContextStore contextStore,
        IAvaloniaLanguageService languageService,
        IAvaloniaDispatcherService dispatcher,
        IAvaloniaTimerFactory timerFactory,
        IOptions<HomogenizationModuleOptions> moduleOptions)
    {
        _contextStore = contextStore;
        _languageService = languageService;
        _dispatcher = dispatcher;
        _timer = timerFactory.Create(TimeSpan.FromMilliseconds(
            Math.Max(200, moduleOptions.Value.Presentation.DataViewRefreshIntervalMs)));
        _timer.Tick += async (_, _) => await RefreshAsync().ConfigureAwait(false);
    }

    public override string ViewId => $"{DependencyInjection.ModuleKey}.DataView";

    public override string ViewTitle
        => ResolveText("Homogenization_Title_Data", "匀浆出料数据");

    public ObservableCollection<HomogenizationDataRow> Records { get; } = [];

    public string StatusText { get; private set; } = string.Empty;

    public bool HasRecords { get; private set; }

    public bool HasNoRecords => !HasRecords;

    public override async Task OnActivatedAsync()
    {
        _timer.Start();
        await RefreshAsync().ConfigureAwait(false);
    }

    public override Task OnDeactivatedAsync()
    {
        _timer.Stop();
        return Task.CompletedTask;
    }

    public async Task RefreshAsync()
    {
        var rows = _contextStore.GetAll()
            .OfType<HomogenizationContext>()
            .SelectMany(static x => x.OutboundRecords)
            .OrderByDescending(static x => x.CompletedTime ?? x.InboundTime ?? DateTime.MinValue)
            .Select(static x => new HomogenizationDataRow(
                x.TrayCode,
                x.InboundTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                x.CompletedTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                x.RuntimeStatus,
                x.RealtimeSnapshot?.StirringSpeed.ToString() ?? "-",
                x.RealtimeSnapshot?.Temperature.ToString() ?? "-",
                x.RealtimeSnapshot?.Vacuum.ToString() ?? "-",
                x.CntActualKg?.ToString() ?? "-",
                x.NmpActualKg?.ToString() ?? "-"))
            .ToArray();

        await _dispatcher.InvokeAsync(() =>
        {
            Records.Clear();
            foreach (var row in rows)
            {
                Records.Add(row);
            }

            HasRecords = rows.Length > 0;
            StatusText = rows.Length == 0
                ? ResolveText("Homogenization_Empty_OutboundRecords", "暂无匀浆出料记录。")
                : string.Format(
                    ResolveText("Homogenization_RecordCountFormat", "共 {0} 条出料记录。"),
                    rows.Length);
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(HasRecords));
            OnPropertyChanged(nameof(HasNoRecords));
            OnPropertyChanged(nameof(ViewTitle));
        }).ConfigureAwait(false);
    }

    private string ResolveText(string key, string fallback)
    {
        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}

public sealed record HomogenizationDataRow(
    string TrayCode,
    string InboundTime,
    string OutboundTime,
    string Status,
    string StirringSpeed,
    string Temperature,
    string Vacuum,
    string CntActual,
    string NmpActual);
