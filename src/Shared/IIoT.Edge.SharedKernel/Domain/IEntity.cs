namespace IIoT.Edge.Common.Domain;

public interface IEntity;

public interface IEntity<TId> : IEntity
{
    TId Id { get; set; }
}