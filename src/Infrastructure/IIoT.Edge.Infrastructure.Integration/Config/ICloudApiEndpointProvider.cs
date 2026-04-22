using IIoT.Edge.Application.Abstractions.Device;

namespace IIoT.Edge.Infrastructure.Integration.Config;

public interface ICloudApiEndpointProvider : ICloudApiPathProvider
{
    string BuildUrl(string relativeOrAbsoluteUrl);
    string GetClientCode();
    string GetDeviceInstancePath();
    string GetBootstrapRefreshPath();
    string GetIdentityDeviceLoginPath();
    string GetHumanIdentityRefreshPath();
    string GetDeviceLogPath();
    string BuildRecipeByDevicePath(Guid deviceId);
}
