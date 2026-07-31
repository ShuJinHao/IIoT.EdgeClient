using IIoT.Edge.Module.Contracts.DataPipeline;

namespace IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;

public sealed record DiagnosticsDeadLetterOperationResult(bool IsSuccess, string Message);

public interface IDiagnosticsDeadLetterOperator
{
    bool CanOperate(DeadLetterRow? row);

    Task<DiagnosticsDeadLetterOperationResult> RequeueAsync(DeadLetterRow row);
}

internal sealed class DiagnosticsDeadLetterOperator(
    IDeadLetterMaintenanceService? deadLetterMaintenanceService = null)
    : IDiagnosticsDeadLetterOperator
{
    private const string ServiceNotRegisteredMessage = "死信运维服务未注册。";

    public bool CanOperate(DeadLetterRow? row)
        => deadLetterMaintenanceService is not null && row is not null;

    public async Task<DiagnosticsDeadLetterOperationResult> RequeueAsync(DeadLetterRow row)
    {
        if (deadLetterMaintenanceService is null)
        {
            return new DiagnosticsDeadLetterOperationResult(false, ServiceNotRegisteredMessage);
        }

        var result = await deadLetterMaintenanceService.RequeueAsync(row.Channel, row.Id);
        return new DiagnosticsDeadLetterOperationResult(result.IsSuccess, result.Message);
    }

}
