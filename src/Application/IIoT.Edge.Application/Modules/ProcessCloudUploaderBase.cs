using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Http;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Application.Modules;

public abstract class ProcessCloudUploaderBase<TCellData, TPayload> : IProcessCloudUploader
    where TCellData : CellDataBase
{
    private readonly ICloudHttpClient _cloudHttp;
    protected readonly ILogService Logger;

    protected ProcessCloudUploaderBase(ICloudHttpClient cloudHttp, ILogService logger)
    {
        _cloudHttp = cloudHttp;
        Logger = logger;
    }

    public abstract string ProcessType { get; }

    public abstract ProcessUploadMode UploadMode { get; }

    protected abstract string UploadPath { get; }

    protected virtual string UploaderName => GetType().Name;

    protected abstract TPayload BuildPayload(
        ProcessCloudUploadContext context,
        IReadOnlyList<TCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records);

    protected virtual Task OnUploadSucceededAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<TCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected virtual Task OnUploadFailedAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CloudCallResult result,
        string message,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task<CloudCallResult> UploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return CloudCallResult.Success();
        }

        var preflightResult = await CheckBeforeUploadAsync(context, records, cancellationToken).ConfigureAwait(false);
        if (preflightResult is not null)
        {
            return preflightResult;
        }

        var cellData = new List<TCellData>(records.Count);
        foreach (var record in records)
        {
            if (record.CellData is not TCellData typed)
            {
                var result = CloudCallResult.Failure(CloudCallOutcome.Exception, "unexpected_process_type");
                var message = $"云端上传器 {UploaderName} 收到不匹配的工序数据：{record.CellData.ProcessType}。";
                Logger.Error($"[Cloud] {message}");
                await OnUploadFailedAsync(context, records, result, message, cancellationToken).ConfigureAwait(false);
                return result;
            }

            cellData.Add(typed);
        }

        return UploadMode == ProcessUploadMode.Batch
            ? await UploadBatchAsync(context, cellData, records, cancellationToken).ConfigureAwait(false)
            : await UploadSingleAsync(context, cellData, records, cancellationToken).ConfigureAwait(false);
    }

    protected virtual Task<CloudCallResult?> CheckBeforeUploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken)
        => Task.FromResult<CloudCallResult?>(null);

    private async Task<CloudCallResult> UploadSingleAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<TCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < records.Count; index++)
        {
            var singleRecord = new[] { records[index] };
            var singleCellData = new[] { cellData[index] };
            var payload = BuildPayload(context, singleCellData, singleRecord);
            var result = await _cloudHttp.PostAsync(
                    UploadPath,
                    payload!,
                    new CloudRequestOptions
                    {
                        IdempotencyKey = CloudIdempotencyKeyBuilder.ForRecord(ProcessType, UploaderName, records[index])
                    })
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                var message = $"云端上传失败，工序 {ProcessType}，数量 1，结果 {result.Outcome}，原因 {result.ReasonCode}。";
                Logger.Error($"[Cloud] {message}");
                await OnUploadFailedAsync(context, singleRecord, result, message, cancellationToken).ConfigureAwait(false);
                return result;
            }

            await OnUploadSucceededAsync(context, singleCellData, singleRecord, cancellationToken).ConfigureAwait(false);
        }

        return CloudCallResult.Success();
    }

    private async Task<CloudCallResult> UploadBatchAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<TCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken)
    {
        var payload = BuildPayload(context, cellData, records);
        var result = await _cloudHttp.PostAsync(
                UploadPath,
                payload!,
                new CloudRequestOptions
                {
                    IdempotencyKey = CloudIdempotencyKeyBuilder.ForBatch(ProcessType, UploaderName, records)
                })
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            var message = $"云端批量上传失败，工序 {ProcessType}，数量 {records.Count}，结果 {result.Outcome}，原因 {result.ReasonCode}。";
            Logger.Error($"[Cloud] {message}");
            await OnUploadFailedAsync(context, records, result, message, cancellationToken).ConfigureAwait(false);
            return result;
        }

        await OnUploadSucceededAsync(context, cellData, records, cancellationToken).ConfigureAwait(false);
        return CloudCallResult.Success();
    }
}
