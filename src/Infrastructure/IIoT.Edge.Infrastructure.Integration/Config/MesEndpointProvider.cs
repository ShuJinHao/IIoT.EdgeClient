using IIoT.Edge.Application.Abstractions.Modules;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Infrastructure.Integration.Config;

public sealed class MesEndpointProvider : IMesEndpointProvider
{
    private readonly IOptionsMonitor<MesApiConfig> _mesApiOptions;

    public MesEndpointProvider(IOptionsMonitor<MesApiConfig> mesApiOptions)
    {
        _mesApiOptions = mesApiOptions;
    }

    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(_mesApiOptions.CurrentValue.BaseUrl);

    public string BuildUrl(string relativeOrAbsoluteUrl)
    {
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        var baseUrl = _mesApiOptions.CurrentValue.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Missing config: MesApi:BaseUrl");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Invalid config: MesApi:BaseUrl = '{baseUrl}'");
        }

        return new Uri(baseUri, relativeOrAbsoluteUrl).ToString();
    }

    public IReadOnlyDictionary<string, string> GetDefaultHeaders()
        => _mesApiOptions.CurrentValue.DefaultHeaders
            .Where(static x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(
                static x => x.Key.Trim(),
                static x => x.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
}
