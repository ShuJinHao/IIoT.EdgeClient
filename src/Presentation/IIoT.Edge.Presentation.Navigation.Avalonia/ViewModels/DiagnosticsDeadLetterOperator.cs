using IIoT.Edge.Application.Abstractions.DataPipeline;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed record AvaloniaDiagnosticsDeadLetterOperationResult(bool IsSuccess, string Message);

public interface IAvaloniaDiagnosticsDeadLetterOperator
{
    bool CanOperate(DiagnosticsDeadLetterRow? row);

    Task<AvaloniaDiagnosticsDeadLetterOperationResult> RequeueAsync(DiagnosticsDeadLetterRow row);

    Task<AvaloniaDiagnosticsDeadLetterOperationResult> DeleteAsync(DiagnosticsDeadLetterRow row);
}

internal sealed class AvaloniaDiagnosticsDeadLetterOperator(
    IDeadLetterMaintenanceService? deadLetterMaintenanceService = null)
    : IAvaloniaDiagnosticsDeadLetterOperator
{
    private const string ServiceNotRegisteredMessage = "死信运维服务未注册。";

    public bool CanOperate(DiagnosticsDeadLetterRow? row)
        => deadLetterMaintenanceService is not null && row is not null;

    public async Task<AvaloniaDiagnosticsDeadLetterOperationResult> RequeueAsync(DiagnosticsDeadLetterRow row)
    {
        if (deadLetterMaintenanceService is null)
        {
            return new AvaloniaDiagnosticsDeadLetterOperationResult(false, ServiceNotRegisteredMessage);
        }

        var result = await deadLetterMaintenanceService.RequeueAsync(row.Channel, row.Id);
        return new AvaloniaDiagnosticsDeadLetterOperationResult(result.IsSuccess, result.Message);
    }

    public async Task<AvaloniaDiagnosticsDeadLetterOperationResult> DeleteAsync(DiagnosticsDeadLetterRow row)
    {
        if (deadLetterMaintenanceService is null)
        {
            return new AvaloniaDiagnosticsDeadLetterOperationResult(false, ServiceNotRegisteredMessage);
        }

        var result = await deadLetterMaintenanceService.DeleteAsync(row.Channel, row.Id);
        return new AvaloniaDiagnosticsDeadLetterOperationResult(result.IsSuccess, result.Message);
    }
}
