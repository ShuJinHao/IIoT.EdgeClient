namespace IIoT.Edge.Application.Common.Crud;

/// <summary>
/// CRUD 命令服务契约。
/// 统一定义保存与删除操作。
/// </summary>
public interface ICrudCommandService<in TSaveDto, TKey>
{
    Task<TKey> SaveAsync(TSaveDto input, CancellationToken cancellationToken = default);
    Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);
}
