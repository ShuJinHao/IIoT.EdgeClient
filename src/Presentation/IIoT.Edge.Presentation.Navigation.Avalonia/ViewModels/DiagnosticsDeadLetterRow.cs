using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

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
