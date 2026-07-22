using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Modules;
using Microsoft.Extensions.Options;

using IIoT.Edge.Module.Contracts.Mes;
namespace IIoT.Edge.Infrastructure.Integration.Config;

public sealed class MesEndpointProvider(
    IModuleParamRoleProvider moduleParamRoleProvider,
    IOptionsMonitor<MesApiConfig> mesApiOptions) : IMesEndpointProvider
{
    public async Task<bool> IsConfiguredAsync(
        string processType,
        CancellationToken cancellationToken = default)
        => HttpUrl.TryCreateHttpBaseUri(
            await GetBaseUrlAsync(processType, cancellationToken).ConfigureAwait(false),
            out _);

    public async Task<string> BuildUrlAsync(
        string processType,
        string relativeOrAbsoluteUrl,
        CancellationToken cancellationToken = default)
    {
        if (HttpUrl.TryCreateAbsoluteHttpUri(relativeOrAbsoluteUrl, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        var baseUrl = await GetBaseUrlAsync(processType, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !HttpUrl.TryCreateHttpBaseUri(baseUrl, out var baseUri))
        {
            throw new InvalidOperationException($"工序 {processType} 未配置 MES 服务地址。");
        }

        return HttpUrl.Build(baseUri, relativeOrAbsoluteUrl).ToString();
    }

    public async Task<string?> TryBuildFirstConfiguredUrlAsync(
        IReadOnlyCollection<string> processTypes,
        string relativeOrAbsoluteUrl,
        CancellationToken cancellationToken = default)
    {
        foreach (var processType in processTypes)
        {
            if (!await IsConfiguredAsync(processType, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            return await BuildUrlAsync(processType, relativeOrAbsoluteUrl, cancellationToken)
                .ConfigureAwait(false);
        }

        return null;
    }

    public IReadOnlyDictionary<string, string> GetDefaultHeaders()
        => mesApiOptions.CurrentValue.DefaultHeaders
            .Where(static x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(
                static x => x.Key.Trim(),
                static x => x.Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

    private Task<string?> GetBaseUrlAsync(
        string processType,
        CancellationToken cancellationToken)
        => moduleParamRoleProvider.GetMesStringAsync(
            processType,
            ModuleParamRole.MesBaseUrl,
            cancellationToken: cancellationToken);
}
