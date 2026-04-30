using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Application.Modules.Cloud;

/// <summary>
/// Cloud 上传通道契约。Application 只定义“某工序数据映射为某类 Cloud payload”的形态，不保存插件业务字段。
/// </summary>
public interface ICloudUploadChannel<TCellData, TPayload> : IProcessCloudUploader
    where TCellData : CellDataBase
{
}
