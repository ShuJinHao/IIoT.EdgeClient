namespace IIoT.Edge.Application.Abstractions.Modules;

public interface IMesHttpClient
{
    Task<bool> PostAsync(
        string url,
        object payload,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);

    Task<string?> PostWithResponseAsync(
        string url,
        object payload,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);

    Task<string?> GetAsync(
        string url,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);
}
