namespace IIoT.Edge.Application.Common.Crud;

/// <summary>
/// CRUD 命令服务基类。
/// 为保存和删除操作提供统一抽象入口。
/// </summary>
public abstract class CrudCommandServiceBase<TSaveDto, TKey>
    : ICrudCommandService<TSaveDto, TKey>
{
    public abstract Task<TKey> SaveAsync(
        TSaveDto input,
        CancellationToken cancellationToken = default);

    public abstract Task DeleteAsync(
        TKey id,
        CancellationToken cancellationToken = default);
}
