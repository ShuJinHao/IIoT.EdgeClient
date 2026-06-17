using IIoT.Edge.Application.Abstractions.Device;

using IIoT.Edge.Application.Abstractions.Cloud;
namespace IIoT.Edge.Infrastructure.Integration.Config;

public interface ICloudApiEndpointProvider : ICloudApiPathProvider
{
    string BuildUrl(string relativeOrAbsoluteUrl);
    string GetClientCode();
    string GetBootstrapSecret();
    string GetDeviceInstancePath();
    string GetBootstrapRefreshPath();
    string GetIdentityDeviceLoginPath();
    string GetHumanIdentityRefreshPath();
    string GetDeviceLogPath();
    string BuildRecipeByDevicePath(Guid deviceId);
}
