using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.SharedKernel.Configuration;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Infrastructure.Integration.Config;

/// <summary>
/// Resolves cloud API absolute URLs and client code from current config.
/// BaseUrl and ClientCode are read from IOptionsMonitor for runtime updates.
/// </summary>
public class CloudApiEndpointProvider : ICloudApiEndpointProvider
{
    private readonly IOptionsMonitor<CloudApiConfig> _cloudApiOptions;
    private readonly ILocalSystemConfigSnapshotReader? _localSystemConfigSnapshotReader;
    private readonly IDevicePluginRuntimeContext? _devicePluginRuntimeContext;

    public CloudApiEndpointProvider(
        IOptionsMonitor<CloudApiConfig> cloudApiOptions,
        ILocalSystemConfigSnapshotReader? localSystemConfigSnapshotReader = null,
        IDevicePluginRuntimeContext? devicePluginRuntimeContext = null)
    {
        _cloudApiOptions = cloudApiOptions;
        _localSystemConfigSnapshotReader = localSystemConfigSnapshotReader;
        _devicePluginRuntimeContext = devicePluginRuntimeContext;
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
            "CloudApi:Paths:DeviceInstance",
            EdgeBindingRouteKey.DeviceInstance);

    public string GetBootstrapRefreshPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.BootstrapRefreshPath) ?? _cloudApiOptions.CurrentValue.Paths.BootstrapRefresh,
            "CloudApi:Paths:BootstrapRefresh",
            EdgeBindingRouteKey.BootstrapRefresh);

    public string GetDeviceActivatePath()
        => RequirePath(
            _cloudApiOptions.CurrentValue.Paths.ActivateDevice,
            "CloudApi:Paths:ActivateDevice",
            EdgeBindingRouteKey.ActivateDevice);

    public string GetDeviceActivateConfirmPath()
        => RequirePath(
            _cloudApiOptions.CurrentValue.Paths.ActivateDeviceConfirm,
            "CloudApi:Paths:ActivateDeviceConfirm",
            EdgeBindingRouteKey.ActivateDeviceConfirm);

    public string GetIdentityDeviceLoginPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.IdentityDeviceLoginPath) ?? _cloudApiOptions.CurrentValue.Paths.IdentityDeviceLogin,
            "CloudApi:Paths:IdentityDeviceLogin",
            EdgeBindingRouteKey.IdentityDeviceLogin);

    public string GetHumanIdentityRefreshPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.HumanIdentityRefreshPath) ?? _cloudApiOptions.CurrentValue.Paths.HumanIdentityRefresh,
            "CloudApi:Paths:HumanIdentityRefresh",
            EdgeBindingRouteKey.HumanIdentityRefresh);

    public string GetHumanSessionValidationPath()
        => RequirePath(
            _cloudApiOptions.CurrentValue.Paths.HumanSessionValidation,
            "CloudApi:Paths:HumanSessionValidation",
            EdgeBindingRouteKey.HumanSessionValidation);

    public string GetDeviceLogPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.DeviceLogPath) ?? _cloudApiOptions.CurrentValue.Paths.DeviceLog,
            "CloudApi:Paths:DeviceLog",
            EdgeBindingRouteKey.DeviceLog);

    public string GetEdgeHostPlcRuntimeStatesPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.EdgeHostPlcRuntimeStatesPath)
                ?? _cloudApiOptions.CurrentValue.Paths.EdgeHostPlcRuntimeStates,
            "CloudApi:Paths:EdgeHostPlcRuntimeStates",
            EdgeBindingRouteKey.EdgeHostPlcRuntimeStates);

    public string GetPassStationBatchPath(string typeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        var template = RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.PassStationBatchTemplatePath)
                ?? _cloudApiOptions.CurrentValue.Paths.PassStationBatchTemplate,
            "CloudApi:Paths:PassStationBatchTemplate",
            EdgeBindingRouteKey.PassStationBatchTemplate);

        return template.Replace(
            "{typeKey}",
            Uri.EscapeDataString(typeKey.Trim().ToLowerInvariant()),
            StringComparison.OrdinalIgnoreCase);
    }

    public string GetCapacityHourlyPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.CapacityHourlyPath) ?? _cloudApiOptions.CurrentValue.Paths.CapacityHourly,
            "CloudApi:Paths:CapacityHourly",
            EdgeBindingRouteKey.CapacityHourly);

    public string GetCapacitySummaryPath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.CapacitySummaryPath) ?? _cloudApiOptions.CurrentValue.Paths.CapacitySummary,
            "CloudApi:Paths:CapacitySummary",
            EdgeBindingRouteKey.CapacitySummary);

    public string GetCapacitySummaryRangePath()
        => RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.CapacitySummaryRangePath) ?? _cloudApiOptions.CurrentValue.Paths.CapacitySummaryRange,
            "CloudApi:Paths:CapacitySummaryRange",
            EdgeBindingRouteKey.CapacitySummaryRange);

    public string BuildRecipeByDevicePath(Guid deviceId)
    {
        var template = RequirePath(
            FirstLocalConfigString(CloudApiConfigParamSchema.RecipeByDeviceTemplatePath) ?? _cloudApiOptions.CurrentValue.Paths.RecipeByDeviceTemplate,
            "CloudApi:Paths:RecipeByDeviceTemplate",
            EdgeBindingRouteKey.RecipeByDeviceTemplate);

        return template.Replace("{deviceId}", deviceId.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private string? FirstLocalConfigString(string key)
        => _devicePluginRuntimeContext?.Current.IsV3 == true
            ? null
            : _localSystemConfigSnapshotReader?
            .GetCurrentSystemConfigs()
            .FirstOrDefault(snapshot => string.Equals(snapshot.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim();

    private static string RequirePath(
        string? configured,
        string key,
        EdgeBindingRouteKey routeKey)
    {
        var value = configured?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing config: {key}");

        try
        {
            return EdgeBindingRouteCatalog.ValidateAndNormalize(routeKey, value);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidOperationException($"Invalid config: {key}", exception);
        }
    }
}
