using System.Collections.ObjectModel;
using System.Windows.Threading;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.Module.Homogenization.Runtime;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.Module.Homogenization.Presentation;

public sealed class HomogenizationDataViewModel : PresentationViewModelBase
{
    private readonly IProductionContextStore _contextStore;
    private readonly DispatcherTimer _timer;

    public HomogenizationDataViewModel(
        IProductionContextStore contextStore,
        HomogenizationModuleOptions moduleOptions)
    {
        _contextStore = contextStore;
        _timer = HomogenizationPresentationHelpers.CreateTimer(
            RefreshAsync,
            moduleOptions.Presentation.DataViewRefreshIntervalMs);
    }

    public override string ViewId => HomogenizationViewIds.DataView;

    public override string ViewTitle => "匀浆产品数据";

    public ObservableCollection<HomogenizationDataRow> Records { get; } = [];

    public override Task OnActivatedAsync()
    {
        _timer.Start();
        return RefreshAsync();
    }

    public override Task OnDeactivatedAsync()
    {
        _timer.Stop();
        return Task.CompletedTask;
    }

    private Task RefreshAsync()
        => RunViewTaskAsync(() =>
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

            ReplaceItems(Records, rows);
            SetStatus(rows.Length == 0 ? "暂无匀浆出料记录。" : $"记录数：{rows.Length}");
            return Task.CompletedTask;
        }, trackBusy: false, clearFeedback: false);
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

internal static class HomogenizationPresentationHelpers
{
    public static DispatcherTimer CreateTimer(Func<Task> refreshAsync, int intervalMs)
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(200, intervalMs))
        };
        timer.Tick += async (_, _) => await refreshAsync();
        return timer;
    }
}
