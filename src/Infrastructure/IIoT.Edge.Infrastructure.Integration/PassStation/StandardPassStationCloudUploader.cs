using System.Text.Json;
using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Http;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(processType);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return CloudCallResult.Success();
        }

        cancellationToken.ThrowIfCancellationRequested();

        var typeKey = NormalizeTypeKey(processType);
        var idempotencyKey = CloudIdempotencyKeyBuilder.ForBatch(typeKey, UploaderName, records);
        var payload = new PassStationBatchUploadPayload(
            context.Device.DeviceId,
            records.Select(static record => CreateItem(record.CellData)).ToArray(),
            idempotencyKey,
            SchemaVersion: 1,
            ProcessType: typeKey);

        try
        {
            return await cloudHttp.PostAsync(
                    pathProvider.GetPassStationBatchPath(typeKey),
                    payload,
                    new CloudRequestOptions
                    {
                        IdempotencyKey = idempotencyKey
                    })
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

    private static PassStationUploadItem CreateItem(CellDataBase cellData)
        => new(
            GetBarcode(cellData),
            FormatCellResult(cellData.CellResult),
            cellData.CompletedTime,
            JsonSerializer.SerializeToElement(cellData, cellData.GetType(), JsonOptions));

    private static string GetBarcode(CellDataBase cellData)
    {
        var displayLabel = cellData.DisplayLabel?.Trim();
        return string.IsNullOrWhiteSpace(displayLabel)
            ? cellData.ProcessType
            : displayLabel;
    }

    private static string? FormatCellResult(bool? cellResult)
        => cellResult switch
        {
            true => "OK",
            false => "NG",
            _ => null
        };

    private static string NormalizeTypeKey(string processType)
        => processType.Trim().ToLowerInvariant();
}

public sealed record PassStationBatchUploadPayload(
    Guid DeviceId,
    IReadOnlyList<PassStationUploadItem> Items,
    string? RequestId = null,
    int SchemaVersion = 1,
    string? ProcessType = null);

public sealed record PassStationUploadItem(
    string Barcode,
    string? CellResult,
    DateTime? CompletedTime,
    JsonElement Payload);
