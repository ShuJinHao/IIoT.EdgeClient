namespace IIoT.Edge.Application.Common.Crud;

/// <summary>
/// CRUD 查询服务基类。
/// 为列表查询和详情查询提供统一抽象入口。
/// </summary>
public abstract class CrudQueryServiceBase<TListDto, TDetailDto, TFilter>
    : ICrudQueryService<TListDto, TDetailDto, TFilter>
{
    public abstract Task<IReadOnlyCollection<TListDto>> GetListAsync(
        TFilter filter,
        CancellationToken cancellationToken = default);

    public abstract Task<TDetailDto?> GetDetailAsync(
        object id,
        CancellationToken cancellationToken = default);
}
