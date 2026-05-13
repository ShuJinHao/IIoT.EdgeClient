namespace IIoT.Edge.Application.Common.Crud;

/// <summary>
/// 实体映射器契约。
/// 用于在不同对象模型之间完成转换。
/// </summary>
public interface IEntityMapper<in TSource, out TDestination>
{
    TDestination Map(TSource source);
}
