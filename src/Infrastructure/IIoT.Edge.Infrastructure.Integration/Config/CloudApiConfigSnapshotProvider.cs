using IIoT.Edge.Application.Abstractions.Config;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Infrastructure.Integration.Config;

public sealed class CloudApiConfigSnapshotProvider(
    IOptionsMonitor<CloudApiConfig> cloudApiOptions) : ICloudApiConfigSnapshotProvider
{
    public CloudApiConfigSnapshot GetCurrent()
    {
        var current = cloudApiOptions.CurrentValue;
        var paths = current.Paths ?? new CloudApiPaths();
        return new CloudApiConfigSnapshot(
            current.BaseUrl ?? string.Empty,
            current.ClientCode ?? string.Empty,
            current.BootstrapSecret ?? string.Empty,
            paths.DeviceInstance ?? string.Empty,
            paths.BootstrapRefresh ?? string.Empty,
            paths.IdentityDeviceLogin ?? string.Empty,
            paths.HumanIdentityRefresh ?? string.Empty,
            paths.DeviceLog ?? string.Empty,
            paths.ProcessUpload ?? string.Empty,
            paths.PassStationBatchTemplate ?? string.Empty,
            paths.CapacityHourly ?? string.Empty,
            paths.CapacitySummary ?? string.Empty,
            paths.CapacitySummaryRange ?? string.Empty,
            paths.RecipeByDeviceTemplate ?? string.Empty,
            paths.ClientReleaseCatalogTemplate ?? string.Empty,
            paths.ClientVersionReport ?? string.Empty,
            current.Enabled);
    }
}
