using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Cloud;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.Homogenization.Integration.Cloud;

/// <summary>
/// 匀浆 Cloud 上传保护器。云端匀浆过站契约尚未单独确认，当前显式跳过上传并避免进入 Cloud retry。
/// </summary>
public sealed class HomogenizationCloudUploader
    : CloudUploadChannelBase<HomogenizationCellData, object>
{
    /// <summary>
    /// 占位路径不会被调用；真正上传必须在云端契约单独确认后再启用。
    /// </summary>
    private const string UploadPathValue = "/disabled/homogenization-cloud-upload";

    public HomogenizationCloudUploader(
        ICloudHttpClient cloudHttp,
        ILogService logger)
        : base(
            DependencyInjection.ModuleKey,
            ProcessUploadMode.Batch,
            UploadPathValue,
            cloudHttp,
            logger)
    {
    }

    protected override Task<CloudCallResult?> CheckBeforeUploadAsync(
        ProcessUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken)
    {
        Logger.Warn(
            $"[Cloud] 匀浆云端上传未启用：云端匀浆过站契约尚未单独确认，已跳过 {records.Count} 条记录且不写入云端重试队列。");
        return Task.FromResult<CloudCallResult?>(CloudCallResult.Success());
    }

    protected override object BuildPayload(
        ProcessUploadContext context,
        IReadOnlyList<HomogenizationCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records)
        => throw new InvalidOperationException("匀浆云端上传未启用，不应构建云端 payload。");
}
