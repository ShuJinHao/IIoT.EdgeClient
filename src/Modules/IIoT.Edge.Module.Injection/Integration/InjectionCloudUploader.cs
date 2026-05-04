using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.Application.Modules.Cloud;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.Injection.Integration;

/// <summary>
/// 注液 Cloud 上传器。上传框架由 Application 提供，本类只负责把注液电芯数据映射为云端批量过站 payload。
/// </summary>
public sealed class InjectionCloudUploader : CloudUploadChannelBase<InjectionCellData, object>
{
    /// <summary>
    /// 注液云端批量过站接口路径。
    /// </summary>
    private const string UploadPathValue = "/api/v1/edge/pass-stations/injection/batch";

    private readonly IProductionTimeProvider _productionTime;

    public InjectionCloudUploader(
        ICloudHttpClient cloudHttp,
        IProductionTimeProvider productionTime,
        ILogService logger)
        : base(DependencyInjection.ModuleKey, ProcessUploadMode.Batch, UploadPathValue, cloudHttp, logger)
    {
        _productionTime = productionTime;
    }

    protected override Task<CloudCallResult?> CheckBeforeUploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken)
    {
        Logger.Warn("[Cloud] 注液云端上传契约未就绪，客户端已跳过上传。");
        return Task.FromResult<CloudCallResult?>(CloudCallResult.Success());
    }

    protected override object BuildPayload(
        ProcessCloudUploadContext context,
        IReadOnlyList<InjectionCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records)
        // 注液当前按批量上传，deviceId 来自 bootstrap 设备身份，items 来自插件内字段映射。
        => new
        {
            deviceId = context.Device.DeviceId,
            items = cellData.Select(ToCloudDto).ToArray()
        };

    private InjectionCloudDto ToCloudDto(InjectionCellData source)
    {
        var completedTime = source.CompletedTime ?? _productionTime.UtcNow;
        var preInjectionTime = source.ScanTime ?? source.CompletedTime ?? _productionTime.UtcNow;
        var postInjectionTime = source.CompletedTime ?? source.ScanTime ?? _productionTime.UtcNow;

        return new InjectionCloudDto
        {
            Barcode = source.Barcode,
            CellResult = source.CellResult == true ? "OK" : "NG",
            CompletedTime = _productionTime.ToBusinessTime(completedTime),
            PreInjectionTime = _productionTime.ToBusinessTime(preInjectionTime),
            PreInjectionWeight = source.PreInjectionWeight,
            PostInjectionTime = _productionTime.ToBusinessTime(postInjectionTime),
            PostInjectionWeight = source.PostInjectionWeight,
            InjectionVolume = source.InjectionVolume
        };
    }
}
