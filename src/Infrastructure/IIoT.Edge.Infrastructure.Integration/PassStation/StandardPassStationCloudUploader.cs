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

        var payloadResult = TryCreatePayload(context, processType, records);
        if (payloadResult.FailureReason is not null)
        {
            return CloudCallResult.Failure(
                CloudCallOutcome.InvalidPayload,
                payloadResult.FailureReason);
        }

        var payload = payloadResult.Payload!;

        try
        {
            return await cloudHttp.PostAsync(
                    pathProvider.GetPassStationBatchPath(payload.ProcessType!),
                    payload,
                    new CloudRequestOptions
                    {
                        IdempotencyKey = payload.RequestId
                    },
                    cancellationToken)
                .ConfigureAwait(false);
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

    private static PayloadCreationResult TryCreatePayload(
        ProcessUploadContext context,
        string? processType,
        IReadOnlyList<CellCompletedRecord> records)
    {
        if (context.Device.DeviceId == Guid.Empty)
        {
            return PayloadCreationResult.Invalid("pass_station_device_id_required");
        }

        var processFailure = ValidateProcessType(processType, out var typeKey);
        if (processFailure is not null)
        {
            return PayloadCreationResult.Invalid(processFailure);
        }

        if (records.Count < PassStationCloudContract.MinItems)
        {
            return PayloadCreationResult.Invalid("pass_station_items_required");
        }

        if (records.Count > PassStationCloudContract.MaxItems)
        {
            return PayloadCreationResult.Invalid("pass_station_items_limit_exceeded");
        }

        var items = new List<PassStationUploadItem>(records.Count);
        foreach (var record in records)
        {
            var itemResult = TryCreateItem(record);
            if (itemResult.FailureReason is not null)
            {
                return PayloadCreationResult.Invalid(itemResult.FailureReason);
            }

            items.Add(itemResult.Item!);
        }

        var requestId = CloudIdempotencyKeyBuilder.ForBatch(typeKey, UploaderName, records);
        if (requestId.Length > PassStationCloudContract.MaxRequestIdLength)
        {
            return PayloadCreationResult.Invalid("pass_station_request_id_too_long");
        }

        return PayloadCreationResult.Valid(new PassStationBatchUploadPayload(
            context.Device.DeviceId,
            items,
            requestId,
            SchemaVersion: PassStationCloudContract.SchemaVersion,
            ProcessType: typeKey));
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

    private sealed record PayloadCreationResult(
        PassStationBatchUploadPayload? Payload,
        string? FailureReason)
    {
        public static PayloadCreationResult Valid(PassStationBatchUploadPayload payload) => new(payload, null);

        public static PayloadCreationResult Invalid(string reason) => new(null, reason);
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
    public const int SchemaVersion = 1;
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
