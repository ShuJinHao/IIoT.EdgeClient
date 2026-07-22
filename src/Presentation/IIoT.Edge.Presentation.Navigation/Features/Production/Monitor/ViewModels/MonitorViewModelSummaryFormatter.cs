using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.UI.Shared.Localization;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;

public interface IMonitorViewModelSummaryFormatter
{
    IReadOnlyList<MonitorStatusItemVm> CreateSummaryItems(DeviceMonitorSnapshot snapshot);

    IReadOnlyList<MonitorStatusItemVm> CreateStateMachineSummaryItems(IReadOnlyList<MonitorStatusItemVm> summaryItems);
}

internal sealed class MonitorViewModelSummaryFormatter(IAppLanguageService languageService)
    : IMonitorViewModelSummaryFormatter
{
    public IReadOnlyList<MonitorStatusItemVm> CreateSummaryItems(DeviceMonitorSnapshot snapshot)
        =>
        [
            new(
                GetText("Navigation_Monitor_ColumnConnectionStatus", "连接状态"),
                FormatConnectionState(snapshot)),
            new(GetText("Navigation_Monitor_Source", "数据来源"), FormatSource(snapshot.Source)),
            new(
                GetText("Navigation_Monitor_ConfigurationState", "配置状态"),
                FormatConfigurationState(snapshot)),
            new(GetText("Navigation_Monitor_ConfigurationEndpoint", "PLC 端点"), snapshot.PlcEndpointText),
            new(GetText("Navigation_Monitor_LastHeartbeat", "最近心跳"), snapshot.LastHeartbeatText),
            new(GetText("Navigation_Monitor_LastUpdated", "最近更新"), snapshot.LastUpdatedText),
            new(GetText("Navigation_Monitor_WipCount", "在制记录"), snapshot.CellCount.ToString()),
            new(GetText("Navigation_Monitor_LastConnected", "最近连接"), snapshot.LastConnectedAtText),
            new(GetText("Navigation_Monitor_LastFailure", "最近异常"), snapshot.LastFailureAtText),
            new(GetText("Navigation_Monitor_LastError", "最后错误"), snapshot.LastErrorText)
        ];

    public IReadOnlyList<MonitorStatusItemVm> CreateStateMachineSummaryItems(IReadOnlyList<MonitorStatusItemVm> summaryItems)
        => summaryItems.Count < 7
            ? summaryItems.Take(Math.Max(0, summaryItems.Count - 1)).ToList()
            : [summaryItems[0], summaryItems[1], summaryItems[2], summaryItems[3], summaryItems[6]];

    private string FormatSource(MonitorSnapshotSource source)
        => source switch
        {
            MonitorSnapshotSource.ProductionContext => GetText(
                "Navigation_Monitor_SourceProductionContext",
                "生产上下文"),
            MonitorSnapshotSource.RuntimeStatus => GetText(
                "Navigation_Monitor_SourceRuntimeStatus",
                "PLC 运行状态"),
            MonitorSnapshotSource.PlcConfiguration => GetText(
                "Navigation_Monitor_SourcePlcConfiguration",
                "PLC 配置"),
            _ => GetText("Navigation_Monitor_SourceUnknown", "未知")
        };

    private string FormatConfigurationState(DeviceMonitorSnapshot snapshot)
    {
        if (!snapshot.HasPlcConfiguration)
        {
            return GetText("Navigation_Monitor_ConfigurationMissing", "未配置");
        }

        return snapshot.IsPlcConfigurationEnabled
            ? GetText("Navigation_Monitor_ConfigurationEnabled", "已启用")
            : GetText("Navigation_Monitor_ConfigurationDisabled", "未启用");
    }

    private string FormatConnectionState(DeviceMonitorSnapshot snapshot)
    {
        if (snapshot.IsConnected)
        {
            return GetText("Navigation_Monitor_ConnectionOnline", "已连接");
        }

        return snapshot.ConnectionState switch
        {
            PlcConnectionState.Connecting => GetText("Navigation_Monitor_ConnectionConnecting", "连接中"),
            PlcConnectionState.Retrying => GetText("Navigation_Monitor_ConnectionRetrying", "重试中"),
            PlcConnectionState.Faulted => GetText("Navigation_Monitor_ConnectionFaulted", "运行异常"),
            _ => GetText("Navigation_Monitor_ConnectionOffline", "未连接")
        };
    }

    private string GetText(string resourceKey, string fallback)
        => languageService.GetString(resourceKey, fallback);
}
