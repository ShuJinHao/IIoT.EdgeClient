namespace IIoT.Edge.SharedKernel.Domain;

/// <summary>
/// 实体基类。
/// 提供统一的主键定义。
/// </summary>
public abstract class BaseEntity<TId> : IEntity<TId>
{
    public TId Id { get; protected set; } = default!;
}
