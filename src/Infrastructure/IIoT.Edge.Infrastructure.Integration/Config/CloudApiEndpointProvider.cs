using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config.CloudApi;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Infrastructure.Integration.Config;

/// <summary>
/// Resolves cloud API absolute URLs and client code from current config.
/// BaseUrl and ClientCode are read from IOptionsMonitor for runtime updates.
/// </summary>
public class CloudApiEndpointProvider : ICloudApiEndpointProvider
{
    private readonly IOptionsMonitor<CloudApiConfig> _cloudApiOptions;
    private readonly ILocalParameterConfigService? _localParameterConfigService;

    public CloudApiEndpointProvider(
        IOptionsMonitor<CloudApiConfig> cloudApiOptions,
        ILocalParameterConfigService? localParameterConfigService = null)
    {
        _cloudApiOptions = cloudApiOptions;
        _localParameterConfigService = localParameterConfigService;
    }

    public string BuildUrl(string relativeOrAbsoluteUrl)
    {
        if (HttpUrl.TryCreateAbsoluteHttpUri(relativeOrAbsoluteUrl, out var absoluteUri))
            return absoluteUri.ToString();

        var baseUrl = FirstLocalConfigString(CloudApiConfigParamSchema.BaseUrl)
            ?? _cloudApiOptions.CurrentValue.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Missing config: CloudApi:BaseUrl");

        if (!HttpUrl.TryCreateHttpBaseUri(baseUrl, out var baseUri))
            throw new InvalidOperationException($"Invalid config: CloudApi:BaseUrl = '{baseUrl}'");

        return HttpUrl.Build(baseUri, relativeOrAbsoluteUrl).ToString();
    }

    public string GetClientCode()
    {
        var configured = FirstLocalConfigString(CloudApiConfigParamSchema.ClientCode)
            ?? _cloudApiOptions.CurrentValue.ClientCode?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        throw new InvalidOperationException("Missing config: CloudApi:ClientCode");
    }

    public string GetBootstrapSecret()
    {
        var configured = FirstLocalConfigString(CloudApiConfigParamSchema.BootstrapSecret)
            ?? _cloudApiOptions.CurrentValue.BootstrapSecret?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        throw new InvalidOperationException("Missing config: CloudApi:BootstrapSecret");
    }

    public string GetDeviceInstancePath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.DeviceInstancePath) ?? _cloudApiOptions.CurrentValue.Paths.DeviceInstance,
            "CloudApi:Paths:DeviceInstance");

    public string GetBootstrapRefreshPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.BootstrapRefreshPath) ?? _cloudApiOptions.CurrentValue.Paths.BootstrapRefresh,
            "CloudApi:Paths:BootstrapRefresh");

    public string GetIdentityDeviceLoginPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.IdentityDeviceLoginPath) ?? _cloudApiOptions.CurrentValue.Paths.IdentityDeviceLogin,
            "CloudApi:Paths:IdentityDeviceLogin");

    public string GetHumanIdentityRefreshPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.HumanIdentityRefreshPath) ?? _cloudApiOptions.CurrentValue.Paths.HumanIdentityRefresh,
            "CloudApi:Paths:HumanIdentityRefresh");

    public string GetDeviceLogPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.DeviceLogPath) ?? _cloudApiOptions.CurrentValue.Paths.DeviceLog,
            "CloudApi:Paths:DeviceLog");

    public string GetEdgeHostPlcRuntimeStatesPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.EdgeHostPlcRuntimeStatesPath)
                ?? _cloudApiOptions.CurrentValue.Paths.EdgeHostPlcRuntimeStates,
            "CloudApi:Paths:EdgeHostPlcRuntimeStates");

    public string GetProcessUploadPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.ProcessUploadPath) ?? _cloudApiOptions.CurrentValue.Paths.ProcessUpload,
            "CloudApi:Paths:ProcessUpload");

    public string GetPassStationBatchPath(string typeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        var template = RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.PassStationBatchTemplatePath)
                ?? _cloudApiOptions.CurrentValue.Paths.PassStationBatchTemplate,
            "CloudApi:Paths:PassStationBatchTemplate");
        if (!template.Contains("{typeKey}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid config: CloudApi:Paths:PassStationBatchTemplate must contain {typeKey}");

        return template.Replace(
            "{typeKey}",
            Uri.EscapeDataString(typeKey.Trim().ToLowerInvariant()),
            StringComparison.OrdinalIgnoreCase);
    }

    public string GetCapacityHourlyPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.CapacityHourlyPath) ?? _cloudApiOptions.CurrentValue.Paths.CapacityHourly,
            "CloudApi:Paths:CapacityHourly");

    public string GetCapacitySummaryPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.CapacitySummaryPath) ?? _cloudApiOptions.CurrentValue.Paths.CapacitySummary,
            "CloudApi:Paths:CapacitySummary");

    public string GetCapacitySummaryRangePath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.CapacitySummaryRangePath) ?? _cloudApiOptions.CurrentValue.Paths.CapacitySummaryRange,
            "CloudApi:Paths:CapacitySummaryRange");

    public string BuildRecipeByDevicePath(Guid deviceId)
    {
        var template = RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.RecipeByDeviceTemplatePath) ?? _cloudApiOptions.CurrentValue.Paths.RecipeByDeviceTemplate,
            "CloudApi:Paths:RecipeByDeviceTemplate");
        if (!template.Contains("{deviceId}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid config: CloudApi:Paths:RecipeByDeviceTemplate must contain {deviceId}");

        return template.Replace("{deviceId}", deviceId.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private string? FirstLocalConfigString(string key)
        => _localParameterConfigService?
            .GetSystemConfigsAsync()
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult()
            .FirstOrDefault(snapshot => string.Equals(snapshot.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();

    private static string RequirePath(string? configured, string key)
    {
        var value = configured?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing config: {key}");

        if (!value.StartsWith('/'))
            throw new InvalidOperationException($"Invalid config: {key} must start with '/'");

        if (value.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidOperationException($"Invalid config: {key} must be a relative API path");

        return value;
    }
}
