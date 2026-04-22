using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Factory;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Infrastructure.DeviceComm.Barcode.Factories;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Factory;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Infrastructure.DeviceComm;

public static class DependencyInjection
{
    public static IServiceCollection AddDeviceCommInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IPlcDataStore, PlcDataStore>();
        services.AddSingleton<IPlcServiceFactory, PlcServiceFactory>();
        services.AddSingleton<PlcRuntimeRegistry>();
        services.AddSingleton<PlcConnectionStatusStore>();
        services.AddSingleton<PlcDeviceRuntimeBuilder>();
        services.AddSingleton<PlcLifecycleCoordinator>();
        services.AddSingleton<IPlcConnectionManager, PlcConnectionManager>();
        services.AddSingleton<IBarcodeReaderFactory, PlcBarcodeReaderFactory>();

        return services;
    }
}
