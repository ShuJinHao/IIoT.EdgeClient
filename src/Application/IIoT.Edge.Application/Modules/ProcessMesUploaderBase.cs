using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Application.Modules;

public abstract class ProcessMesUploaderBase<TCellData> : IProcessMesUploader
    where TCellData : CellDataBase
{
    protected readonly ILogService Logger;

    protected ProcessMesUploaderBase(ILogService logger)
    {
        Logger = logger;
    }

    public abstract string ProcessType { get; }

    public virtual MesUploadMode UploadMode => MesUploadMode.Single;

    protected abstract Task<MesCallResult> UploadCellAsync(
        ProcessMesUploadContext context,
        TCellData cellData,
        CellCompletedRecord record,
        CancellationToken cancellationToken);

    public async Task<MesCallResult> UploadAsync(
        ProcessMesUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return MesCallResult.Success("没有需要上传 MES 的记录。");
        }

        foreach (var record in records)
        {
            if (record.CellData is not TCellData cellData)
            {
                var message = $"MES 上传器 {GetType().Name} 收到不匹配的工序数据：{record.CellData.ProcessType}。";
                Logger.Error($"[MES] {message}");
                return MesCallResult.InvalidContext(message);
            }

            var result = await UploadCellAsync(context, cellData, record, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                Logger.Error($"[MES] 工序 {ProcessType} 上传失败：{result.Message}");
                return result;
            }
        }

        return MesCallResult.Success();
    }
}
