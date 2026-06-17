namespace IIoT.Edge.Application.Abstractions.Mes;

public interface IMesHttpClient
{
    Task<bool> PostAsync(
        string processType,
        string url,
        object payload,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);

    Task<string?> PostWithResponseAsync(
        string processType,
        string url,
        object payload,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);

    Task<string?> GetAsync(
        string processType,
        string url,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);
}
