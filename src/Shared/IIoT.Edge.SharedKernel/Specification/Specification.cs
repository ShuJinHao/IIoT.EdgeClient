using IIoT.Edge.SharedKernel.Domain;
using System.Linq.Expressions;

namespace IIoT.Edge.SharedKernel.Specification;

/// <summary>
/// 规约基类。
/// 为具体规约提供通用的过滤、排序、分组、分页和包含配置能力。
/// </summary>
public abstract class Specification<T> : ISpecification<T> where T : class, IEntity
{
    public Expression<Func<T, bool>>? FilterCondition { get; protected init; }
    public List<Expression<Func<T, object>>> Includes { get; } = [];
    public List<string> IncludeStrings { get; } = [];
    public Expression<Func<T, object>>? OrderBy { get; private set; }
    public Expression<Func<T, object>>? OrderByDescending { get; private set; }
    public Expression<Func<T, object>>? GroupBy { get; private set; }
    public int Take { get; private set; }
    public int Skip { get; private set; }
    public bool IsPagingEnabled { get; private set; }

    protected void AddInclude(Expression<Func<T, object>> includeExpression)
        => Includes.Add(includeExpression);

    protected void AddInclude(string includeString)
        => IncludeStrings.Add(includeString);

    protected void SetPaging(int skip, int take)
    {
        Skip = skip;
        Take = take;
        IsPagingEnabled = true;
    }

    protected void SetOrderBy(Expression<Func<T, object>> orderByExpression)
        => OrderBy = orderByExpression;

    protected void SetOrderByDescending(Expression<Func<T, object>> orderByDescExpression)
        => OrderByDescending = orderByDescExpression;

    protected void SetGroupBy(Expression<Func<T, object>> groupByExpression)
        => GroupBy = groupByExpression;
}
