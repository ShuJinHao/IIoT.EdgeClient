using System.Text.Json.Serialization;
using IIoT.Edge.Module.Homogenization.Payload;

namespace IIoT.Edge.Module.Homogenization.Integration;

/// <summary>
/// 匀浆工序上传到 Cloud process-records 入口的批量 payload。
/// </summary>
public sealed record HomogenizationProcessRecordsCloudPayload(
    [property: JsonPropertyName("typeKey")] string TypeKey,
    [property: JsonPropertyName("processType")] string ProcessType,
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("deviceId")] Guid DeviceId,
    [property: JsonPropertyName("records")] IReadOnlyList<HomogenizationProcessRecordCloudPayload> Records);

/// <summary>
/// 单条匀浆过站记录的 Cloud payload。
/// </summary>
public sealed record HomogenizationProcessRecordCloudPayload(
    [property: JsonPropertyName("typeKey")] string TypeKey,
    [property: JsonPropertyName("processType")] string ProcessType,
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("deviceId")] Guid DeviceId,
    [property: JsonPropertyName("barcode")] string Barcode,
    [property: JsonPropertyName("cellResult")] bool? CellResult,
    [property: JsonPropertyName("completedTime")] DateTime? CompletedTime,
    [property: JsonPropertyName("payload")] HomogenizationProcessRecordBusinessCloudPayload Payload);

/// <summary>
/// 匀浆插件自有业务字段，作为 Cloud process-records 的 payload 扩展段。
/// </summary>
public sealed record HomogenizationProcessRecordBusinessCloudPayload(
    [property: JsonPropertyName("plcName")] string PlcName,
    [property: JsonPropertyName("deviceCode")] string DeviceCode,
    [property: JsonPropertyName("inboundTime")] DateTime? InboundTime,
    [property: JsonPropertyName("runtimeStatus")] string RuntimeStatus,
    [property: JsonPropertyName("realtimeSnapshot")] HomogenizationRealtimeSnapshot? RealtimeSnapshot,
    [property: JsonPropertyName("recipeSnapshot")] HomogenizationRecipeSnapshot? RecipeSnapshot,
    [property: JsonPropertyName("equipmentStatusSnapshot")] HomogenizationEquipmentStatusSnapshot? EquipmentStatusSnapshot,
    [property: JsonPropertyName("cntActualKg")] double? CntActualKg,
    [property: JsonPropertyName("cntTargetKg")] double? CntTargetKg,
    [property: JsonPropertyName("cntTankAWeightKg")] double? CntTankAWeightKg,
    [property: JsonPropertyName("cntTankBWeightKg")] double? CntTankBWeightKg,
    [property: JsonPropertyName("nmpActualKg")] double? NmpActualKg,
    [property: JsonPropertyName("nmpTargetKg")] double? NmpTargetKg,
    [property: JsonPropertyName("glueActualKg")] double? GlueActualKg,
    [property: JsonPropertyName("setStirringTimeMinutes")] int? SetStirringTimeMinutes,
    [property: JsonPropertyName("remainingStirringTimeMinutes")] int? RemainingStirringTimeMinutes,
    [property: JsonPropertyName("setDispersionTimeMinutes")] int? SetDispersionTimeMinutes,
    [property: JsonPropertyName("remainingDispersionTimeMinutes")] int? RemainingDispersionTimeMinutes,
    [property: JsonPropertyName("batchNumber")] string? BatchNumber,
    [property: JsonPropertyName("mainBatchPlan")] string? MainBatchPlan);
