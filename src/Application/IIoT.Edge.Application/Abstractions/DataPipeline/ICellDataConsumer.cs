using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline;

/// <summary>
/// 电芯数据消费者接口。
/// 
/// ProcessQueueTask 会按顺序依次调用各个消费者。
/// 任意一步失败都不会阻塞后续消费者。
/// 
/// FailureMode 用于声明失败后的处理语义：
///   BestEffort：仅记录日志，不要求补偿
///   Durable：必须进入本地补偿链路，不能静默丢失
///
/// RetryChannel 用于决定 Durable 失败后的补传通道：
///   null：失败后不进入重传队列，例如纯本地操作的 Excel、界面通知
///   "Cloud"：进入 Cloud 通道的补传队列
///   "MES"：进入 MES 通道的补传队列
/// </summary>
public interface ICellDataConsumer
{
    /// <summary>
    /// 消费者标识，供日志记录和重传定位使用。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 执行顺序，数字越小越先执行。
    /// </summary>
    int Order { get; }

    /// <summary>
    /// 失败语义声明。
    /// Durable 消费者必须配置 RetryChannel，避免静默丢失。
    /// </summary>
    ConsumerFailureMode FailureMode { get; }

    /// <summary>
    /// 失败后进入哪个补传通道。
    /// null 表示不补传；"Cloud" 和 "MES" 表示按对应通道补传。
    /// </summary>
    string? RetryChannel { get; }

    /// <summary>
    /// 处理一条电芯完成记录。
    /// 返回 true 表示成功，返回 false 表示失败，是否补传由 RetryChannel 决定。
    /// </summary>
    Task<bool> ProcessAsync(CellCompletedRecord record);
}
