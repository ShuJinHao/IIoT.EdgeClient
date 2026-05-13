namespace IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;

/// <summary>
/// 设备日志同步任务。
/// 60 秒间隔执行。内存队列达到阈值后按批提交到云端；失败时写入 SQLite 备份缓冲。
/// </summary>
public interface IDeviceLogSyncTask : ISyncTask { }
