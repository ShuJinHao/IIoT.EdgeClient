using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Sdk.Cloud;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using IIoT.Edge.Infrastructure.Integration;

namespace IIoT.Edge.Infrastructure.Integration.PassStation;

/// <summary>
/// 宿主标准过站 Cloud 上传器。插件只声明 CloudUploadMode，过站信封由宿主统一生成。
/// </summary>
#pragma warning disable CS0618 // Cloud v2 transport context remains the governed Host ABI in SDK 2.0.13.
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
                var routeTypeKey = payload.TypeKey ?? payload.ProcessType;
                result = await cloudHttp.PostAsync(
                        pathProvider.GetPassStationBatchPath(routeTypeKey!),
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
        var identity = ResolveIdentity(
            context: null,
            processType,
            [record],
            requireDeviceMatch: false);
        if (identity.FailureReason is not null)
        {
            return CloudCallResult.Failure(CloudCallOutcome.InvalidPayload, identity.FailureReason);
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

        var identity = ResolveIdentity(context, processType, records, requireDeviceMatch: true);
        if (identity.FailureReason is not null)
        {
            return PayloadsCreationResult.Invalid(identity.FailureReason);
        }

        if (records.Count < PassStationCloudContract.MinItems)
        {
            return PayloadsCreationResult.Invalid("pass_station_items_required");
        }

        if (records.Count > PassStationCloudContract.MaxItems)
        {
            return PayloadsCreationResult.Invalid("pass_station_items_limit_exceeded");
        }

        var legacyV1Items = new List<PassStationUploadItem>(records.Count);
        var legacyV1Records = new List<CellCompletedRecord>(records.Count);
        var legacyV2Items = new List<PassStationUploadItem>(records.Count);
        var legacyV2Records = new List<CellCompletedRecord>(records.Count);
        var v3Items = new List<PassStationUploadItem>(records.Count);
        var v3Records = new List<CellCompletedRecord>(records.Count);
        foreach (var record in records)
        {
            var itemResult = TryCreateItem(record);
            if (itemResult.FailureReason is not null)
            {
                return PayloadsCreationResult.Invalid(itemResult.FailureReason);
            }

            // Binding v3 is data-capability driven: the generic Host validates only the
            // envelope/identity and emits schema v3. Cloud validates TypeKey and payload fields
            // against the exact installed plugin's data-capabilities.json. Current AP/CP/MG
            // facts must never become Host framework constraints.
            if (identity.IsV3)
            {
                v3Items.Add(itemResult.Item!);
                v3Records.Add(record);
            }
            else if (IsLegacyStrictV2Eligible(identity.TypeKey, record, itemResult.Item!))
            {
                legacyV2Items.Add(itemResult.Item!);
                legacyV2Records.Add(record);
            }
            else
            {
                legacyV1Items.Add(itemResult.Item!);
                legacyV1Records.Add(record);
            }
        }

        var payloads = new List<PassStationBatchUploadPayload>(3);
        if (v3Items.Count > 0)
            payloads.Add(CreatePayload(context, identity, v3Records, v3Items, PassStationCloudContract.StrictSchemaVersion));
        if (legacyV2Items.Count > 0)
            payloads.Add(CreatePayload(context, identity, legacyV2Records, legacyV2Items, PassStationCloudContract.LegacyStrictSchemaVersion));
        if (legacyV1Items.Count > 0)
            payloads.Add(CreatePayload(context, identity, legacyV1Records, legacyV1Items, PassStationCloudContract.LegacySchemaVersion));

        return payloads.Any(payload => payload.RequestId!.Length > PassStationCloudContract.MaxRequestIdLength)
            ? PayloadsCreationResult.Invalid("pass_station_request_id_too_long")
            : PayloadsCreationResult.Valid(payloads);
    }

    private static PassStationBatchUploadPayload CreatePayload(
        ProcessUploadContext context,
        UploadIdentity identity,
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
            CloudIdempotencyKeyBuilder.ForBatch(identity.TypeKey, uploaderKey, records),
            schemaVersion,
            identity.IsV3 ? identity.ProcessType : identity.TypeKey,
            identity.IsV3 ? identity.ClientCode : null,
            identity.IsV3 ? identity.TypeKey : null);
    }

    private static UploadIdentity ResolveIdentity(
        ProcessUploadContext? context,
        string? processType,
        IReadOnlyList<CellCompletedRecord> records,
        bool requireDeviceMatch)
    {
        if (string.IsNullOrWhiteSpace(processType))
        {
            return UploadIdentity.Invalid("pass_station_process_type_required");
        }

        var normalizedProcessType = processType.Trim().ToLowerInvariant();
        if (normalizedProcessType.Length > PassStationCloudContract.MaxProcessTypeLength)
        {
            return UploadIdentity.Invalid("pass_station_process_type_too_long");
        }

        var identityKinds = records
            .Select(DataPipelineRecordIdentityClassifier.Classify)
            .Distinct()
            .ToArray();
        if (identityKinds.Length == 0
            || identityKinds is [DataPipelineRecordIdentityKind.LegacyV2])
        {
            return UploadIdentity.Valid(
                isV3: false,
                clientCode: string.Empty,
                processType: normalizedProcessType,
                typeKey: normalizedProcessType);
        }

        if (identityKinds.Any(static kind => kind != DataPipelineRecordIdentityKind.CompleteV3))
        {
            return UploadIdentity.Invalid("pass_station_v3_identity_incomplete");
        }

        var clientCodes = records
            .Select(static record => record.ClientCode.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var processTypes = records
            .Select(static record => record.ProcessType.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var typeKeys = records
            .Select(static record => record.TypeKey.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var completionIds = records
            .Select(static record => record.CompletionId.Trim())
            .ToArray();
        if (clientCodes.Length != 1)
        {
            return UploadIdentity.Invalid("pass_station_client_code_mixed");
        }

        if (processTypes.Length != 1 || processTypes[0] != normalizedProcessType)
        {
            return UploadIdentity.Invalid("pass_station_process_type_mixed");
        }

        if (typeKeys.Length != 1)
        {
            return UploadIdentity.Invalid("pass_station_type_key_mixed");
        }

        if (completionIds.Any(static value => value.Length > PassStationCloudContract.MaxCompletionIdLength))
        {
            return UploadIdentity.Invalid("pass_station_completion_id_too_long");
        }
        if (completionIds.Distinct(StringComparer.Ordinal).Count() != completionIds.Length)
        {
            return UploadIdentity.Invalid("pass_station_completion_id_duplicate");
        }

        if (typeKeys[0].Length > PassStationCloudContract.MaxTypeKeyLength)
        {
            return UploadIdentity.Invalid("pass_station_type_key_too_long");
        }

        if (requireDeviceMatch)
        {
            var sessionClientCode = context?.Device.ClientCode?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(sessionClientCode)
                || !string.Equals(sessionClientCode, clientCodes[0], StringComparison.Ordinal))
            {
                return UploadIdentity.Invalid("pass_station_session_client_code_mismatch");
            }
        }

        return UploadIdentity.Valid(
            isV3: true,
            clientCode: clientCodes[0],
            processType: normalizedProcessType,
            typeKey: typeKeys[0]);
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
            extensionPayload)
        {
            CompletionId = NormalizeOptional(record.CompletionId),
            ClientCode = NormalizeOptional(record.ClientCode)?.ToUpperInvariant(),
            TypeKey = NormalizeOptional(record.TypeKey)?.ToLowerInvariant(),
            PlcCode = NormalizeOptional(record.ResolvePlcCode())
        });
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsLegacyStrictV2Eligible(
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

    private sealed record PayloadsCreationResult(
        IReadOnlyList<PassStationBatchUploadPayload> Payloads,
        string? FailureReason)
    {
        public static PayloadsCreationResult Valid(IReadOnlyList<PassStationBatchUploadPayload> payloads) => new(payloads, null);

        public static PayloadsCreationResult Invalid(string reason) => new([], reason);
    }

    private sealed record UploadIdentity(
        bool IsV3,
        string ClientCode,
        string ProcessType,
        string TypeKey,
        string? FailureReason)
    {
        public static UploadIdentity Valid(
            bool isV3,
            string clientCode,
            string processType,
            string typeKey)
            => new(isV3, clientCode, processType, typeKey, null);

        public static UploadIdentity Invalid(string reason)
            => new(false, string.Empty, string.Empty, string.Empty, reason);
    }

    private sealed record ItemCreationResult(
        PassStationUploadItem? Item,
        string? FailureReason)
    {
        public static ItemCreationResult Valid(PassStationUploadItem item) => new(item, null);

        public static ItemCreationResult Invalid(string reason) => new(null, reason);
    }
}
#pragma warning restore CS0618

internal static class PassStationCloudContract
{
    public const int LegacySchemaVersion = 1;
    public const int LegacyStrictSchemaVersion = 2;
    public const int StrictSchemaVersion = 3;
    public const int SchemaVersion = LegacySchemaVersion;
    public const int MinItems = 1;
    public const int MaxItems = 1000;
    public const int MaxProcessTypeLength = 32;
    public const int MaxTypeKeyLength = 128;
    public const int MaxCompletionIdLength = 128;
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
    string? ProcessType = null,
    string? ClientCode = null,
    string? TypeKey = null);

public sealed record PassStationUploadItem(
    string Barcode,
    string CellResult,
    DateTime CompletedTime,
    JsonElement Payload)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompletionId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TypeKey { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PlcCode { get; init; }
}
