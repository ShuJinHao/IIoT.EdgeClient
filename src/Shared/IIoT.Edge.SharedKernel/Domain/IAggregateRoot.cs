namespace IIoT.Edge.SharedKernel.Domain;

/// <summary>
/// 聚合根标记接口。
/// </summary>
public interface IAggregateRoot : IEntity;

/// <summary>
/// 带主键类型的聚合根标记接口。
/// </summary>
public interface IAggregateRoot<TId> : IEntity<TId>;
