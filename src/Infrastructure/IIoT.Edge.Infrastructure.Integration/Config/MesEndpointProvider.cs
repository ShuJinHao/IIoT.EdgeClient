using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Infrastructure.Integration.Config;

public sealed class MesEndpointProvider(
    IModuleParamRoleProvider moduleParamRoleProvider,
    IOptionsMonitor<MesApiConfig> mesApiOptions) : IMesEndpointProvider
{
    public async Task<bool> IsConfiguredAsync(
        string processType,
        CancellationToken cancellationToken = default)
        => Uri.TryCreate(
            await GetBaseUrlAsync(processType, cancellationToken).ConfigureAwait(false),
            UriKind.Absolute,
            out _);

    public async Task<string> BuildUrlAsync(
        string processType,
        string relativeOrAbsoluteUrl,
        CancellationToken cancellationToken = default)
    {
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        var baseUrl = await GetBaseUrlAsync(processType, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"工序 {processType} 未配置 MES 服务地址。");
        }

        return new Uri(baseUri, relativeOrAbsoluteUrl).ToString();
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
        => moduleParamRoleProvider.GetStringAsync(
            processType,
            ModuleParamCategory.Mes,
            ModuleParamRole.MesBaseUrl,
            cancellationToken: cancellationToken);
}
