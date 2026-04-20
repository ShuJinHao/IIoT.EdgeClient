namespace IIoT.Edge.Application.Abstractions.Context;

public sealed record ProductionContextPersistenceDiagnostics(
    int CorruptFileCount,
    DateTime? LastCorruptDetectedAt);
