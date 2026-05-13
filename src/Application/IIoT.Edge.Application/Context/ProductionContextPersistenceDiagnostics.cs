namespace IIoT.Edge.Application.Context;

public sealed record ProductionContextPersistenceDiagnostics(
    int CorruptFileCount,
    DateTime? LastCorruptDetectedAt);
