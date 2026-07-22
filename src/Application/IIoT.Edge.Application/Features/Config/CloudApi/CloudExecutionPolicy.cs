using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Config;

namespace IIoT.Edge.Application.Features.Config.CloudApi;

public sealed class CloudExecutionPolicy(
    ILocalSystemRuntimeConfigService runtimeConfig) : ICloudExecutionPolicy
{
    public bool IsEnabled => runtimeConfig.Current.SystemCloudEnabled;
}
