using IIoT.Edge.Common.Domain;
using System.Linq.Expressions;

namespace IIoT.Edge.Common.Specification;

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