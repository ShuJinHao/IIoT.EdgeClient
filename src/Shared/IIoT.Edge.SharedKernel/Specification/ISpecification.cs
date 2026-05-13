using IIoT.Edge.SharedKernel.Domain;
using System.Linq.Expressions;

namespace IIoT.Edge.SharedKernel.Specification;

/// <summary>
/// 规约契约。
/// 用于定义过滤、排序、分组、分页与导航属性包含规则。
/// </summary>
public interface ISpecification<T> where T : class, IEntity
{
    Expression<Func<T, bool>>? FilterCondition { get; }
    List<Expression<Func<T, object>>> Includes { get; }
    List<string> IncludeStrings { get; }
    Expression<Func<T, object>>? OrderBy { get; }
    Expression<Func<T, object>>? OrderByDescending { get; }
    Expression<Func<T, object>>? GroupBy { get; }
    int Take { get; }
    int Skip { get; }
    bool IsPagingEnabled { get; }
}
