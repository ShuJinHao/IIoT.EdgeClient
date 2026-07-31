using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

namespace IIoT.Edge.Host.DataPipeline.Services;

internal static class DataPipelineLogContext
{
    public static string Format(CellCompletedRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(record.CellData);
        return FormatCore(
            DataPipelineCompletionIdentity.Create(record),
            record.ResolvePlcCode(),
            record.ModuleId,
            record.CellData.ProcessType,
            record.TaskKey,
            record.CellData.DisplayLabel);
    }

    public static string Format(FailedCellRecord record, CellDataBase? cellData = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        var correlationId = TryCreateCompletionId(record, cellData)
            ?? $"{Normalize(record.Channel, "Retry")}:{record.Id}";
        var businessId = cellData?.DisplayLabel;
        if (string.IsNullOrWhiteSpace(businessId))
        {
            businessId = !string.IsNullOrWhiteSpace(record.TraceBatchNumber)
                ? record.TraceBatchNumber
                : !string.IsNullOrWhiteSpace(record.MainPlanCode)
                    ? record.MainPlanCode
                    : $"RetryRecord:{record.Id}";
        }

        return FormatCore(
            correlationId,
            record.PlcCode,
            record.ModuleId,
            record.ProcessType,
            record.TaskKey,
            businessId);
    }

    public static string FormatFallback(IFallbackRecord record, CellDataBase? cellData = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        var source = new FailedCellRecord
        {
            Id = record.Id,
            Channel = "Fallback",
            ProcessType = record.ProcessType,
            CellDataJson = record.CellDataJson,
            FailedTarget = record.FailedTarget,
            PlcCode = record.PlcCode,
            NetworkDeviceId = record.NetworkDeviceId,
            DeviceName = record.DeviceName,
            ModuleId = record.ModuleId,
            TaskKey = record.TaskKey,
            PlanSessionId = record.PlanSessionId,
            MainPlanCode = record.MainPlanCode,
            TraceBatchNumber = record.TraceBatchNumber,
            IdempotencyKeyVersion = record.IdempotencyKeyVersion
        };
        return Format(source, cellData);
    }

    private static string? TryCreateCompletionId(FailedCellRecord record, CellDataBase? cellData)
    {
        if (cellData is null)
        {
            return null;
        }

        try
        {
            return DataPipelineCompletionIdentity.Create(
                DataPipelineRetryChannelMetadata.CreateCompletedRecord(record, cellData));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string FormatCore(
        string correlationId,
        string? plcCode,
        string? moduleId,
        string? processType,
        string? taskKey,
        string? businessId)
        => $"[CorrelationId={Normalize(correlationId, "Unknown")}]" +
           $"[PlcCode={Normalize(plcCode, "Unresolved")}]" +
           $"[ModuleId={Normalize(moduleId, "Unresolved")}]" +
           $"[ProcessType={Normalize(processType, "Unresolved")}]" +
           $"[TaskKey={Normalize(taskKey, "Unresolved")}]" +
           $"[BusinessId={Normalize(businessId, "Unresolved")}]";

    private static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim()
            .Replace('\r', '_')
            .Replace('\n', '_')
            .Replace(']', '_');
        return normalized.Length <= 128 ? normalized : normalized[..128];
    }
}
