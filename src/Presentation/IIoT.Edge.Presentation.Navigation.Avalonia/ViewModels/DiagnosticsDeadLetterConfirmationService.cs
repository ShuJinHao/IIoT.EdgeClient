using System.Globalization;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public interface IAvaloniaDiagnosticsDeadLetterConfirmationService
{
    Task<bool> ConfirmRequeueAsync(DiagnosticsDeadLetterRow row);

    Task<bool> ConfirmDeleteAsync(DiagnosticsDeadLetterRow row);
}

internal sealed class AvaloniaDiagnosticsDeadLetterConfirmationService(
    IAvaloniaLanguageService languageService,
    IAvaloniaDialogService dialogService)
    : IAvaloniaDiagnosticsDeadLetterConfirmationService
{
    public Task<bool> ConfirmRequeueAsync(DiagnosticsDeadLetterRow row)
        => dialogService.ConfirmAsync(
            Text("Navigation_Diagnostics_ConfirmRequeueTitle", "确认重新入队"),
            Format(
                "Navigation_Diagnostics_ConfirmRequeueMessageFormat",
                "即将把{0}死信记录重新写入对应 retry 队列。ID：{1}；工序：{2}；目标：{3}。成功后该记录会从死信表移除，后续由正常补传任务处理。是否继续？",
                FormatChannel(row.Channel),
                row.Id,
                row.ProcessType,
                row.FailedTarget));

    public Task<bool> ConfirmDeleteAsync(DiagnosticsDeadLetterRow row)
        => dialogService.ConfirmAsync(
            Text("Navigation_Diagnostics_ConfirmDeleteTitle", "确认删除死信"),
            Format(
                "Navigation_Diagnostics_ConfirmDeleteMessageFormat",
                "即将删除{0}死信记录。ID：{1}；工序：{2}；目标：{3}。删除只会移除本地死信记录，不会补传，且不可恢复。是否继续？",
                FormatChannel(row.Channel),
                row.Id,
                row.ProcessType,
                row.FailedTarget));

    private string FormatChannel(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => Text("Navigation_Diagnostics_ChannelCloud", "云端"),
            DataPipelineRetryChannel.Mes => Text("Navigation_Diagnostics_ChannelMes", "MES"),
            _ => channel.ToString()
        };

    private string Text(string key, string fallback)
    {
        var value = languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    private string Format(string key, string fallback, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Text(key, fallback), args);
}
