using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Common.Http;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

using IIoT.Edge.Application.Abstractions.Cloud;
namespace IIoT.Edge.Application.Modules.Cloud;

/// <summary>
/// Cloud 上传通道基类。负责类型校验、单条/批量发送、幂等键和结果回调，插件只实现 payload 映射。
/// </summary>
public abstract class CloudUploadChannelBase<TCellData, TPayload>
    : ICloudUploadChannel<TCellData, TPayload>
    where TCellData : CellDataBase
{
    private readonly ICloudHttpClient _cloudHttp;
    private readonly string _processType;
    private readonly ProcessUploadMode _uploadMode;
    private readonly string _uploadPath;
    protected readonly ILogService Logger;

    protected CloudUploadChannelBase(
        string processType,
        ProcessUploadMode uploadMode,
        string uploadPath,
        ICloudHttpClient cloudHttp,
        ILogService logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processType);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadPath);

        _processType = processType;
        _uploadMode = uploadMode;
        _uploadPath = uploadPath;
        _cloudHttp = cloudHttp;
        Logger = logger;
    }

    public string ProcessType => _processType;

    public ProcessUploadMode UploadMode => _uploadMode;

    protected string UploadPath => _uploadPath;

    protected virtual string UploaderName => GetType().Name;

    /// <summary>
    /// 插件侧唯一必须实现的 Cloud payload 映射点；字段含义应留在具体插件中说明。
    /// </summary>
    protected abstract TPayload BuildPayload(
        ProcessUploadContext context,
        IReadOnlyList<TCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records);

    /// <summary>
    /// 上传成功后的插件钩子，用于写诊断或运行态，不参与 Cloud/MES 补偿调度。
    /// </summary>
    protected virtual Task OnUploadSucceededAsync(
        ProcessUploadContext context,
        IReadOnlyList<TCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <summary>
    /// 上传失败后的插件钩子，用于写诊断或运行态；实际 retry/fallback 由 Runtime DataPipeline 处理。
    /// </summary>
    protected virtual Task OnUploadFailedAsync(
        ProcessUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CloudCallResult result,
        string message,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public async Task<CloudCallResult> UploadAsync(
        ProcessUploadContext context,
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
                Logger.Error($"[云端] {message}");
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
        ProcessUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken)
        => Task.FromResult<CloudCallResult?>(null);

    private async Task<CloudCallResult> UploadSingleAsync(
        ProcessUploadContext context,
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
                Logger.Error($"[云端] {message}");
                await OnUploadFailedAsync(context, singleRecord, result, message, cancellationToken).ConfigureAwait(false);
                return result;
            }

            await OnUploadSucceededAsync(context, singleCellData, singleRecord, cancellationToken).ConfigureAwait(false);
        }

        return CloudCallResult.Success();
    }

    private async Task<CloudCallResult> UploadBatchAsync(
        ProcessUploadContext context,
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
            Logger.Error($"[云端] {message}");
            await OnUploadFailedAsync(context, records, result, message, cancellationToken).ConfigureAwait(false);
            return result;
        }

        await OnUploadSucceededAsync(context, cellData, records, cancellationToken).ConfigureAwait(false);
        return CloudCallResult.Success();
    }
}
