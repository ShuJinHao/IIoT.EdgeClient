namespace IIoT.Edge.Application.Common.Crud;

/// <summary>
/// CRUD 查询服务契约。
/// 统一定义列表查询与详情查询操作。
/// </summary>
public interface ICrudQueryService<TListDto, TDetailDto, in TFilter>
{
    Task<IReadOnlyCollection<TListDto>> GetListAsync(TFilter filter, CancellationToken cancellationToken = default);
    Task<TDetailDto?> GetDetailAsync(object id, CancellationToken cancellationToken = default);
}
