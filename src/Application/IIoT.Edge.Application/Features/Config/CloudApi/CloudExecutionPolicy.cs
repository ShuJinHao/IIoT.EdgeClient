using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Application.Features.Config.CloudApi;

public sealed class CloudExecutionPolicy(
    ILocalSystemRuntimeConfigService runtimeConfig) : ICloudExecutionPolicy
{
    public bool IsEnabled => runtimeConfig.Current.SystemCloudEnabled;
}
