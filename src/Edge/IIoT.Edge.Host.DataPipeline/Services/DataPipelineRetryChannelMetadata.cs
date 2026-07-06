using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Host.DataPipeline.Services;

internal static class DataPipelineRetryChannelMetadata
{
    public static bool ShouldProcess(CellCompletedRecord record, ICellDataConsumer consumer)
        => consumer.RetryChannel switch
        {
            DataPipelineRetryChannel.Cloud => record.CellData.UploadTargets.HasFlag(DataPipelineUploadTargets.Cloud),
            DataPipelineRetryChannel.Mes => record.CellData.UploadTargets.HasFlag(DataPipelineUploadTargets.Mes),
            _ => true
        };

    public static string Format(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => "云端",
            DataPipelineRetryChannel.Mes => "MES",
            DataPipelineRetryChannel.None => "未配置",
            _ => channel.ToString()
        };

    public static string? TryGetFailedRecordSourceTable(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => "failed_cloud_records",
            DataPipelineRetryChannel.Mes => "failed_mes_records",
            _ => null
        };

    public static string GetFailedRecordSourceTable(DataPipelineRetryChannel channel)
        => TryGetFailedRecordSourceTable(channel)
           ?? throw new InvalidOperationException($"不支持的补偿链路：{channel}。");

    public static string GetFallbackRecordSourceTable(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => "cloud_fallback_records",
            DataPipelineRetryChannel.Mes => "mes_fallback_records",
            _ => throw new InvalidOperationException($"不支持的兜底链路：{channel}。")
        };

    public static DataPipelineDeadLetterChannel CreateDeadLetterChannel(DataPipelineRetryChannel channel)
        => channel switch
        {
            DataPipelineRetryChannel.Cloud => new DataPipelineDeadLetterChannel(
                LogPrefix: "云端补传",
                DeadLetterName: "Cloud",
                CriticalSource: "Retry.CloudDeadLetterPersistFailed"),
            DataPipelineRetryChannel.Mes => new DataPipelineDeadLetterChannel(
                LogPrefix: "MES补传",
                DeadLetterName: "MES",
                CriticalSource: "Retry.MesDeadLetterPersistFailed"),
            _ => throw new InvalidOperationException($"不支持的死信链路：{channel}。")
        };

    public static CellCompletedRecord CreateCompletedRecord(FailedCellRecord record, CellDataBase cellData)
        => new()
        {
            CellData = cellData,
            NetworkDeviceId = record.NetworkDeviceId,
            DeviceName = record.DeviceName,
            ModuleId = record.ModuleId,
            TaskKey = record.TaskKey,
            PlanSessionId = record.PlanSessionId,
            MainPlanCode = record.MainPlanCode,
            TraceBatchNumber = record.TraceBatchNumber,
            CreatedAtUtc = DateTime.SpecifyKind(record.CreatedAt, DateTimeKind.Utc)
        };

    public static string ResolveLogDeviceName(IReadOnlyList<FailedCellRecord> records)
    {
        var deviceNames = records
            .Select(record => record.DeviceName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return deviceNames.Count == 1 ? deviceNames[0] : "多PLC";
    }

    public static FailedRecordSourceKey CreateSourceKey(FailedCellRecord record)
        => new(record.NetworkDeviceId, (record.DeviceName ?? string.Empty).Trim());
}

internal readonly record struct FailedRecordSourceKey(int? NetworkDeviceId, string DeviceName);
