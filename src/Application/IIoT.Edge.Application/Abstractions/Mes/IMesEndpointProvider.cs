namespace IIoT.Edge.Application.Abstractions.Mes;

public interface IMesEndpointProvider
{
    Task<bool> IsConfiguredAsync(string processType, CancellationToken cancellationToken = default);

    Task<string> BuildUrlAsync(
        string processType,
        string relativeOrAbsoluteUrl,
        CancellationToken cancellationToken = default);

    Task<string?> TryBuildFirstConfiguredUrlAsync(
        IReadOnlyCollection<string> processTypes,
        string relativeOrAbsoluteUrl,
        CancellationToken cancellationToken = default);

    IReadOnlyDictionary<string, string> GetDefaultHeaders();
}
