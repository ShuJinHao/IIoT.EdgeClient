namespace IIoT.Edge.Application.Abstractions.Cache;

/// <summary>
/// 通用边缘缓存服务契约。
/// 负责内存缓存的读取、写入、删除与按前缀清理。
/// </summary>
public interface IEdgeCacheService
{
    /// <summary>
    /// 获取缓存值；未命中时返回默认值。
    /// </summary>
    T? Get<T>(string key);

    /// <summary>
    /// 写入缓存值。
    /// </summary>
    void Set<T>(string key, T value);

    /// <summary>
    /// 删除单个缓存项。
    /// </summary>
    void Remove(string key);

    /// <summary>
    /// 按前缀批量删除缓存项，例如 <c>DeviceParam:10</c>。
    /// </summary>
    void RemoveByPrefix(string prefix);

    /// <summary>
    /// 清空全部缓存。
    /// </summary>
    void Clear();

    /// <summary>
    /// 判断指定键是否存在于缓存中。
    /// </summary>
    bool Contains(string key);

    /// <summary>
    /// 获取缓存值；未命中时由同一个 key 的单一加载器构建，避免并发击穿底层存储。
    /// </summary>
    Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? absoluteExpirationRelativeToNow = null,
        TimeSpan? nullValueExpirationRelativeToNow = null,
        CancellationToken cancellationToken = default);
}
