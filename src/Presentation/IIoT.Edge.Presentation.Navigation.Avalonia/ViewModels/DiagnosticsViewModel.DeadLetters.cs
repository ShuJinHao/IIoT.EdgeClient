using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed partial class DiagnosticsViewModel
{
    private void ApplyDeadLetterRows(EdgeSyncDiagnosticsSnapshot snapshot)
    {
        Replace(CloudDeadLetters, BuildDeadLetterRows(DataPipelineRetryChannel.Cloud, snapshot.Cloud.DeadLetters));
        Replace(MesDeadLetters, BuildDeadLetterRows(DataPipelineRetryChannel.Mes, snapshot.Mes.DeadLetters));
        RefreshDeadLetterCommandState();
    }

    private static IReadOnlyList<DiagnosticsDeadLetterRow> BuildDeadLetterRows(
        DataPipelineRetryChannel channel,
        DeadLetterDiagnosticsSnapshot? snapshot)
        => snapshot?.LatestRecords
            .Select(record => DiagnosticsDeadLetterRow.From(channel, record, FormatTime((DateTime?)record.CreatedAt)))
            .ToArray()
        ?? [];

    private bool CanOperateDeadLetter(DiagnosticsDeadLetterRow? row)
        => CanOperateDeadLetters && (_deadLetterOperator?.CanOperate(row) ?? false);

    private async Task RequeueDeadLetterAsync(DiagnosticsDeadLetterRow? row)
    {
        if (row is null || !EnsureCanOperateDeadLetters())
        {
            return;
        }

        if (_deadLetterConfirmationService is null)
        {
            FeedbackMessage = "死信确认服务未注册。";
            return;
        }

        if (!await _deadLetterConfirmationService.ConfirmRequeueAsync(row))
        {
            FeedbackMessage = Text("Navigation_Diagnostics_RequeueCanceled", "已取消死信重新入队。");
            return;
        }

        var result = _deadLetterOperator is null
            ? new AvaloniaDiagnosticsDeadLetterOperationResult(false, "死信运维服务未注册。")
            : await _deadLetterOperator.RequeueAsync(row);

        if (result.IsSuccess)
        {
            await RefreshAsync();
        }

        FeedbackMessage = result.Message;
    }

    private async Task DeleteDeadLetterAsync(DiagnosticsDeadLetterRow? row)
    {
        if (row is null || !EnsureCanOperateDeadLetters())
        {
            return;
        }

        if (_deadLetterConfirmationService is null)
        {
            FeedbackMessage = "死信确认服务未注册。";
            return;
        }

        if (!await _deadLetterConfirmationService.ConfirmDeleteAsync(row))
        {
            FeedbackMessage = Text("Navigation_Diagnostics_DeleteCanceled", "已取消死信删除。");
            return;
        }

        var result = _deadLetterOperator is null
            ? new AvaloniaDiagnosticsDeadLetterOperationResult(false, "死信运维服务未注册。")
            : await _deadLetterOperator.DeleteAsync(row);

        if (result.IsSuccess)
        {
            await RefreshAsync();
        }

        FeedbackMessage = result.Message;
    }

    private bool EnsureCanOperateDeadLetters()
    {
        if (CanOperateDeadLetters)
        {
            return true;
        }

        FeedbackMessage = Text(
            "Navigation_Diagnostics_AdminRequired",
            "当前账号不是本地管理员，不能执行死信运维操作。");
        return false;
    }

    private void StartDeadLetterPermissionObserving()
    {
        if (_isObservingPermission || _permissionService is null)
        {
            return;
        }

        _permissionService.PermissionStateChanged += HandlePermissionStateChanged;
        _isObservingPermission = true;
        RefreshDeadLetterCommandState();
    }

    private void StopDeadLetterPermissionObserving()
    {
        if (!_isObservingPermission || _permissionService is null)
        {
            return;
        }

        _permissionService.PermissionStateChanged -= HandlePermissionStateChanged;
        _isObservingPermission = false;
    }

    private void HandlePermissionStateChanged()
    {
        var dispatcher = ResolveOptional<IAvaloniaDispatcherService>();
        if (dispatcher is null)
        {
            RefreshDeadLetterCommandState();
            return;
        }

        dispatcher.Post(RefreshDeadLetterCommandState);
    }

    private void RefreshDeadLetterCommandState()
    {
        OnPropertyChanged(nameof(CanOperateDeadLetters));
        _requeueDeadLetterCommand.NotifyCanExecuteChanged();
        _deleteDeadLetterCommand.NotifyCanExecuteChanged();
    }

    private string Text(string key, string fallback)
    {
        var value = _languageService.GetText(key);
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}

public sealed record DiagnosticsDeadLetterRow(
    DataPipelineRetryChannel Channel,
    long Id,
    string ProcessType,
    string FailedTarget,
    string FailureStage,
    string Source,
    string CreatedAt,
    string FailureReason,
    string CellDataJson)
{
    public static DiagnosticsDeadLetterRow From(
        DataPipelineRetryChannel channel,
        DeadLetterRecord record,
        string createdAt)
        => new(
            channel,
            record.Id,
            Normalize(record.ProcessType),
            Normalize(record.FailedTarget),
            Normalize(record.FailureStage),
            $"{Normalize(record.SourceTable)}/{record.SourceRecordId?.ToString() ?? "--"}",
            createdAt,
            Normalize(record.FailureReason),
            Normalize(record.CellDataJson));

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? "--" : value;
}
