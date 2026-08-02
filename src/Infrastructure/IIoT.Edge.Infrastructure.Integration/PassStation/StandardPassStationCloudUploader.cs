using System.Globalization;
using System.Text.Json;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Sdk.Cloud;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

namespace IIoT.Edge.Infrastructure.Integration.PassStation;

/// <summary>
/// 宿主标准过站 Cloud 上传器。插件只声明 CloudUploadMode，过站信封由宿主统一生成。
/// </summary>
public sealed class StandardPassStationCloudUploader(
    ICloudApiPathProvider pathProvider,
    ICloudHttpClient cloudHttp)
{
    private const string UploaderName = nameof(StandardPassStationCloudUploader);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CloudCallResult> UploadAsync(
        ProcessUploadContext context,
        string processType,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(records);

        cancellationToken.ThrowIfCancellationRequested();

        var batchesResult = TryCreatePayloads(context, processType, records);
        if (batchesResult.FailureReason is not null)
        {
            return CloudCallResult.Failure(
                CloudCallOutcome.InvalidPayload,
                batchesResult.FailureReason);
        }

        try
        {
            CloudCallResult result = CloudCallResult.Success();
            foreach (var payload in batchesResult.Payloads)
            {
                result = await cloudHttp.PostAsync(
                        pathProvider.GetPassStationBatchPath(payload.ProcessType!),
                        payload,
                        new CloudRequestOptions
                        {
                            IdempotencyKey = payload.RequestId
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!result.IsSuccess)
                    return result;
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return CloudCallResult.Failure(
                CloudCallOutcome.Exception,
                "standard_pass_station_upload_exception");
        }
    }

    internal static CloudCallResult ValidateRecord(
        string? processType,
        CellCompletedRecord record)
    {
        var processFailure = ValidateProcessType(processType, out _);
        if (processFailure is not null)
        {
            return CloudCallResult.Failure(CloudCallOutcome.InvalidPayload, processFailure);
        }

        var itemResult = TryCreateItem(record);
        return itemResult.FailureReason is null
            ? CloudCallResult.Success()
            : CloudCallResult.Failure(CloudCallOutcome.InvalidPayload, itemResult.FailureReason);
    }

    private static PayloadsCreationResult TryCreatePayloads(
        ProcessUploadContext context,
        string? processType,
        IReadOnlyList<CellCompletedRecord> records)
    {
        if (context.Device.DeviceId == Guid.Empty)
        {
            return PayloadsCreationResult.Invalid("pass_station_device_id_required");
        }

        var processFailure = ValidateProcessType(processType, out var typeKey);
        if (processFailure is not null)
        {
            return PayloadsCreationResult.Invalid(processFailure);
        }

        if (records.Count < PassStationCloudContract.MinItems)
        {
            return PayloadsCreationResult.Invalid("pass_station_items_required");
        }

        if (records.Count > PassStationCloudContract.MaxItems)
        {
            return PayloadsCreationResult.Invalid("pass_station_items_limit_exceeded");
        }

        var legacyItems = new List<PassStationUploadItem>(records.Count);
        var legacyRecords = new List<CellCompletedRecord>(records.Count);
        var strictItems = new List<PassStationUploadItem>(records.Count);
        var strictRecords = new List<CellCompletedRecord>(records.Count);
        foreach (var record in records)
        {
            var itemResult = TryCreateItem(record);
            if (itemResult.FailureReason is not null)
            {
                return PayloadsCreationResult.Invalid(itemResult.FailureReason);
            }

            if (IsStrictV2Eligible(typeKey, record, itemResult.Item!))
            {
                strictItems.Add(itemResult.Item!);
                strictRecords.Add(record);
            }
            else
            {
                legacyItems.Add(itemResult.Item!);
                legacyRecords.Add(record);
            }
        }

        var payloads = new List<PassStationBatchUploadPayload>(2);
        if (strictItems.Count > 0)
            payloads.Add(CreatePayload(context, typeKey, strictRecords, strictItems, PassStationCloudContract.StrictSchemaVersion));
        if (legacyItems.Count > 0)
            payloads.Add(CreatePayload(context, typeKey, legacyRecords, legacyItems, PassStationCloudContract.LegacySchemaVersion));

        return payloads.Any(payload => payload.RequestId!.Length > PassStationCloudContract.MaxRequestIdLength)
            ? PayloadsCreationResult.Invalid("pass_station_request_id_too_long")
            : PayloadsCreationResult.Valid(payloads);
    }

    private static PassStationBatchUploadPayload CreatePayload(
        ProcessUploadContext context,
        string typeKey,
        IReadOnlyList<CellCompletedRecord> records,
        IReadOnlyList<PassStationUploadItem> items,
        int schemaVersion)
    {
        var uploaderKey = schemaVersion == PassStationCloudContract.LegacySchemaVersion
            ? UploaderName
            : $"{UploaderName}:v{schemaVersion}";
        return new PassStationBatchUploadPayload(
            context.Device.DeviceId,
            items,
            CloudIdempotencyKeyBuilder.ForBatch(typeKey, uploaderKey, records),
            schemaVersion,
            typeKey);
    }

    private static string? ValidateProcessType(string? processType, out string typeKey)
    {
        if (string.IsNullOrWhiteSpace(processType))
        {
            typeKey = string.Empty;
            return "pass_station_process_type_required";
        }

        typeKey = NormalizeTypeKey(processType);
        return typeKey.Length > PassStationCloudContract.MaxProcessTypeLength
            ? "pass_station_process_type_too_long"
            : null;
    }

    private static ItemCreationResult TryCreateItem(CellCompletedRecord record)
    {
        if (record.CellData is null)
        {
            return ItemCreationResult.Invalid("pass_station_payload_required");
        }

        var barcode = GetBarcode(record.CellData);
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return ItemCreationResult.Invalid("pass_station_barcode_required");
        }

        if (barcode.Length > PassStationCloudContract.MaxBarcodeLength)
        {
            return ItemCreationResult.Invalid("pass_station_barcode_too_long");
        }

        if (!record.CellData.CellResult.HasValue)
        {
            return ItemCreationResult.Invalid("pass_station_cell_result_required");
        }

        if (!record.CellData.CompletedTime.HasValue)
        {
            return ItemCreationResult.Invalid("pass_station_completed_time_required");
        }

        if (!IsReasonableTimestamp(record.CellData.CompletedTime.Value))
        {
            return ItemCreationResult.Invalid("pass_station_completed_time_out_of_range");
        }

        JsonElement extensionPayload;
        try
        {
            extensionPayload = JsonSerializer.SerializeToElement(
                record.CellData,
                record.CellData.GetType(),
                JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return ItemCreationResult.Invalid("pass_station_payload_invalid");
        }

        if (extensionPayload.ValueKind != JsonValueKind.Object)
        {
            return ItemCreationResult.Invalid("pass_station_payload_object_required");
        }

        if (extensionPayload.EnumerateObject().Take(PassStationCloudContract.MaxPayloadProperties + 1).Count()
            > PassStationCloudContract.MaxPayloadProperties)
        {
            return ItemCreationResult.Invalid("pass_station_payload_properties_limit_exceeded");
        }

        return ItemCreationResult.Valid(new PassStationUploadItem(
            barcode,
            FormatCellResult(record.CellData.CellResult.Value),
            record.CellData.CompletedTime.Value,
            extensionPayload));
    }

    private static bool IsStrictV2Eligible(
        string typeKey,
        CellCompletedRecord record,
        PassStationUploadItem item)
    {
        if (typeKey is not "ap" and not "cp"
            || item.CompletedTime.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        var payload = item.Payload;
        if (!TryGetRequiredString(payload, "plcCode", 64, out _)
            || !TryGetRequiredString(payload, "plcName", 128, out _)
            || !TryGetRequiredString(payload, "clipSlot", 3, out var clipSlot)
            || clipSlot is not "MG1" and not "MG2"
            || !TryGetUtcTimestamp(payload, "startTime", out var startTime)
            || startTime > item.CompletedTime
            || !TryGetNonNegativeInteger(payload, "punchingQuantity")
            || !TryGetNonNegativeDecimal(payload, "punchingSpeed", 5))
        {
            return false;
        }

        return record.CellData.CellResult.HasValue
               && item.CellResult is PassStationCloudContract.EmittedOk or PassStationCloudContract.EmittedNg;
    }

    private static bool TryGetRequiredString(
        JsonElement payload,
        string propertyName,
        int maxLength,
        out string value)
    {
        value = string.Empty;
        if (!payload.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length is > 0 && value.Length <= maxLength;
    }

    private static bool TryGetUtcTimestamp(
        JsonElement payload,
        string propertyName,
        out DateTime utcValue)
    {
        utcValue = default;
        if (!payload.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(
                property.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed)
            || parsed.Offset != TimeSpan.Zero)
        {
            return false;
        }

        utcValue = parsed.UtcDateTime;
        return IsReasonableTimestamp(utcValue);
    }

    private static bool TryGetNonNegativeInteger(JsonElement payload, string propertyName)
        => payload.TryGetProperty(propertyName, out var property)
           && property.ValueKind == JsonValueKind.Number
           && property.TryGetInt64(out var value)
           && value >= 0;

    private static bool TryGetNonNegativeDecimal(
        JsonElement payload,
        string propertyName,
        int maxScale)
    {
        if (!payload.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetDecimal(out var value)
            || value < 0)
        {
            return false;
        }

        var scale = (decimal.GetBits(value)[3] >> 16) & 0x7f;
        return scale <= maxScale;
    }

    private static bool IsReasonableTimestamp(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        return utcValue >= PassStationCloudContract.MinimumCompletedTimeUtc
               && utcValue <= DateTime.UtcNow.AddDays(PassStationCloudContract.MaximumCompletedTimeOffsetDays);
    }

    private static string GetBarcode(CellDataBase cellData)
    {
        var displayLabel = cellData.DisplayLabel?.Trim();
        return string.IsNullOrWhiteSpace(displayLabel)
            ? cellData.ProcessType.Trim()
            : displayLabel;
    }

    private static string FormatCellResult(bool cellResult)
        => cellResult ? "OK" : "NG";

    private static string NormalizeTypeKey(string processType)
        => processType.Trim().ToLowerInvariant();

    private sealed record PayloadsCreationResult(
        IReadOnlyList<PassStationBatchUploadPayload> Payloads,
        string? FailureReason)
    {
        public static PayloadsCreationResult Valid(IReadOnlyList<PassStationBatchUploadPayload> payloads) => new(payloads, null);

        public static PayloadsCreationResult Invalid(string reason) => new([], reason);
    }

    private sealed record ItemCreationResult(
        PassStationUploadItem? Item,
        string? FailureReason)
    {
        public static ItemCreationResult Valid(PassStationUploadItem item) => new(item, null);

        public static ItemCreationResult Invalid(string reason) => new(null, reason);
    }
}

internal static class PassStationCloudContract
{
    public const int LegacySchemaVersion = 1;
    public const int StrictSchemaVersion = 2;
    public const int SchemaVersion = LegacySchemaVersion;
    public const int MinItems = 1;
    public const int MaxItems = 1000;
    public const int MaxProcessTypeLength = 32;
    public const int MaxRequestIdLength = 128;
    public const int MaxBarcodeLength = 128;
    public const int MaxCellResultLength = 32;
    public const int MaxPayloadProperties = 64;
    public const int MaximumCompletedTimeOffsetDays = 1;
    public const string EmittedOk = "OK";
    public const string EmittedNg = "NG";

    public static readonly DateTime MinimumCompletedTimeUtc =
        new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}

public sealed record PassStationBatchUploadPayload(
    Guid DeviceId,
    IReadOnlyList<PassStationUploadItem> Items,
    string? RequestId = null,
    int SchemaVersion = 1,
    string? ProcessType = null);

public sealed record PassStationUploadItem(
    string Barcode,
    string CellResult,
    DateTime CompletedTime,
    JsonElement Payload);
