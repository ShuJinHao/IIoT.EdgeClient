namespace IIoT.Edge.Application.Abstractions.Shared;

public interface IRetryDiagnosticsStore<TRuntimeState>
    where TRuntimeState : struct, Enum
{
    void SetRuntimeState(TRuntimeState state);
}
