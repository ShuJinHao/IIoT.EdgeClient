
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Application.Abstractions.Context;

/// <summary>
/// 生产上下文管理接口。
/// 
/// 按 DeviceName 管理所有 PLC 设备的 ProductionContext。
/// DeviceName 是全局唯一的聚合键。
/// 
/// 实现方负责内存管理和 JSON 持久化。
/// </summary>
public interface IProductionContextStore
{
    /// <summary>
    /// 获取指定设备的 Context，不存在时自动创建。
    /// </summary>
    ProductionContext GetOrCreate(string deviceName);

    /// <summary>
    /// 获取所有 Context，供界面展示和上传遍历使用。
    /// </summary>
    IReadOnlyCollection<ProductionContext> GetAll();

    /// <summary>
    /// 获取本地持久化坏档诊断信息。
    /// </summary>
    ProductionContextPersistenceDiagnostics GetPersistenceDiagnostics();

    /// <summary>
    /// 启动时从本地文件恢复状态。
    /// </summary>
    void LoadFromFile();

    /// <summary>
    /// 退出时或定时任务触发时，将状态保存到本地文件。
    /// </summary>
    void SaveToFile();

    /// <summary>
    /// 启动定时自动保存。
    /// </summary>
    Task StartAutoSaveAsync(CancellationToken ct, int intervalSeconds = 30);
}
