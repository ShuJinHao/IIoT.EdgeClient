namespace IIoT.Edge.Application.Abstractions.Config;

public interface ILocalSystemRuntimeConfigService
{
    SystemRuntimeConfigSnapshot Current { get; }

    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);
}
