namespace IIoT.Edge.Application.Abstractions.Modules;

public interface IRetryDiagnosticsStore<TRuntimeState>
    where TRuntimeState : struct, Enum
{
    void SetRuntimeState(TRuntimeState state);
}
