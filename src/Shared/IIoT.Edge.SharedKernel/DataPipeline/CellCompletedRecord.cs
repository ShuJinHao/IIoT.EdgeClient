using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.SharedKernel.DataPipeline;

/// <summary>
/// 电芯完成记录。
/// 作为数据管道中传递单次完工结果的载体。
/// </summary>
public class CellCompletedRecord
{
    /// <summary>
    /// 电芯生产数据。
    /// 具体子类由工序决定，消费者可根据类型或 <c>ProcessType</c> 判断。
    /// </summary>
    public CellDataBase CellData { get; set; } = null!;

    /// <summary>
    /// 产生该记录的 PLC 网络设备数据库 Id。为空时允许从 <see cref="CellDataBase.PlcDeviceId"/> 回退。
    /// </summary>
    public int? NetworkDeviceId { get; set; }

    /// <summary>
    /// 产生该记录的 PLC 设备号。用于日志、补传、死信和 UI 归属，不作为 Cloud 设备主键。
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// 产生该记录的模块 Id，例如 DieCuttingCathode。
    /// </summary>
    public string ModuleId { get; set; } = string.Empty;

    /// <summary>
    /// 产生该记录的 PLC 业务任务 key。
    /// </summary>
    public string TaskKey { get; set; } = string.Empty;

    /// <summary>
    /// 本次运行主批计划会话 Id。用于防止跨启动复用历史主批计划。
    /// </summary>
    public string PlanSessionId { get; set; } = string.Empty;

    /// <summary>
    /// 当前主批计划号。
    /// </summary>
    public string MainPlanCode { get; set; } = string.Empty;

    /// <summary>
    /// MES 生成的追溯批次号。
    /// </summary>
    public string TraceBatchNumber { get; set; } = string.Empty;

    /// <summary>
    /// 记录进入业务队列前的创建时间。
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public int? ResolveNetworkDeviceId()
        => NetworkDeviceId ?? CellData?.PlcDeviceId;

    public string ResolveDeviceName()
        => !string.IsNullOrWhiteSpace(DeviceName)
            ? DeviceName
            : CellData?.DeviceName ?? string.Empty;
}
