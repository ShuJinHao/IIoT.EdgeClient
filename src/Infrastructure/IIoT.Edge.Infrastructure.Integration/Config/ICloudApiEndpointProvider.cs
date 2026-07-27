using IIoT.Edge.Module.Contracts.Device;

using IIoT.Edge.Module.Contracts.Cloud;
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
    string GetHumanSessionValidationPath();
    string GetDeviceLogPath();
    string GetEdgeHostPlcRuntimeStatesPath();
    string BuildRecipeByDevicePath(Guid deviceId);
}
