namespace IIoT.Edge.Common.Domain;

public interface IAggregateRoot : IEntity;

public interface IAggregateRoot<TId> : IEntity<TId>;