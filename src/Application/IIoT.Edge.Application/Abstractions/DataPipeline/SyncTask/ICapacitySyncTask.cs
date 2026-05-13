namespace IIoT.Edge.Application.Abstractions.DataPipeline.SyncTask;

/// <summary>
/// 产能定时同步任务。
/// 以 60 秒为间隔运行，在线时提交产能快照，失败时写入 SQLite 离线缓冲。
/// </summary>
public interface ICapacitySyncTask : ISyncTask { }
