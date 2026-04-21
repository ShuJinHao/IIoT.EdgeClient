using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Infrastructure.Integration.Config;

public sealed class MesEndpointProvider : IMesEndpointProvider
{
    private readonly IOptionsMonitor<MesApiConfig> _mesApiOptions;
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;

    public MesEndpointProvider(
        IOptionsMonitor<MesApiConfig> mesApiOptions,
        ILocalSystemRuntimeConfigService runtimeConfig)
    {
        _mesApiOptions = mesApiOptions;
        _runtimeConfig = runtimeConfig;
    }

    public bool IsConfigured
        => TryResolveConfiguredBaseUri(out _);

    public string BuildUrl(string relativeOrAbsoluteUrl)
    {
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        if (!TryResolveConfiguredBaseUri(out var baseUri))
        {
            throw new InvalidOperationException("Missing config: MesApi:BaseUrl");
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

    private bool TryResolveConfiguredBaseUri(out Uri baseUri)
    {
        var runtimeBaseUrl = _runtimeConfig.Current.MesBaseUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(runtimeBaseUrl)
            && Uri.TryCreate(runtimeBaseUrl, UriKind.Absolute, out var runtimeBaseUri))
        {
            baseUri = runtimeBaseUri;
            return true;
        }

        var configuredBaseUrl = _mesApiOptions.CurrentValue.BaseUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl)
            && Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var configuredBaseUri))
        {
            baseUri = configuredBaseUri;
            return true;
        }

        baseUri = default!;
        return false;
    }
}
