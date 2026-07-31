using System.Threading;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public interface IDiagnosticsDeadLetterCommandWorkflow
{
    bool CanOperate(DeadLetterRow? row);

    Task RequeueAsync(DeadLetterRow row);
}

internal sealed class DiagnosticsDeadLetterCommandWorkflow(
    IDiagnosticsViewModelCallback callback,
    IDiagnosticsDeadLetterOperator deadLetterOperator,
    IDiagnosticsDeadLetterConfirmationService confirmationService)
    : IDiagnosticsDeadLetterCommandWorkflow
{
    public bool CanOperate(DeadLetterRow? row)
        => callback.CanOperateDeadLetters && deadLetterOperator.CanOperate(row);

    public async Task RequeueAsync(DeadLetterRow row)
    {
        try
        {
            if (!EnsureCanOperateDeadLetters())
            {
                return;
            }

            if (!await confirmationService.ConfirmRequeueAsync(row))
            {
                callback.SetStatus(callback.GetText("Navigation_Diagnostics_RequeueCanceled", "已取消死信重新入队。"));
                return;
            }

            var result = await deadLetterOperator.RequeueAsync(row);
            if (result.IsSuccess)
            {
                await callback.RefreshAsync(CancellationToken.None);
                callback.SetStatus(result.Message);
                return;
            }

            callback.SetError(result.Message);
        }
        catch (Exception ex)
        {
            callback.SetError(callback.FormatText(
                "Navigation_Diagnostics_RequeueFailedFormat",
                "死信重新入队失败：{0}",
                ex.GetType().Name));
        }
    }

    private bool EnsureCanOperateDeadLetters()
    {
        if (callback.CanOperateDeadLetters)
        {
            return true;
        }

        callback.SetError(callback.GetText(
            "Navigation_Diagnostics_AdminRequired",
            "当前账号不是本地管理员，不能执行死信运维操作。"));
        return false;
    }
}
