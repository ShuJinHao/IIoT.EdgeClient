namespace IIoT.Edge.SharedKernel.Domain;

/// <summary>
/// 实体标记接口。
/// </summary>
public interface IEntity;

/// <summary>
/// 带主键类型的实体接口。
/// </summary>
public interface IEntity<TId> : IEntity
{
    TId Id { get; }
}
