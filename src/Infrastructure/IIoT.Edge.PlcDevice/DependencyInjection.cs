using IIoT.Edge.Contracts.Plc.Factory;
using IIoT.Edge.Contracts.Plc.Store;
using IIoT.Edge.PlcDevice.Proxy;
using IIoT.Edge.PlcDevice.Store;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.PlcDevice;

public static class DependencyInjection
{
    public static IServiceCollection AddPlcDevice(this IServiceCollection services)
    {
        services.AddSingleton<IPlcDataStore, PlcDataStore>();
        services.AddSingleton<IPlcServiceFactory, PlcServiceFactory>();

        return services;
    }
}