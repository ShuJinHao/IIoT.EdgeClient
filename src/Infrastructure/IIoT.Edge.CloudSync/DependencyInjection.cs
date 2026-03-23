using IIoT.Edge.CloudSync.Auth;
using IIoT.Edge.CloudSync.Config;
using IIoT.Edge.CloudSync.Device;
using IIoT.Edge.Contracts.Auth;
using IIoT.Edge.Contracts.Device;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.CloudSync;

public static class DependencyInjection
{
    public static IServiceCollection AddCloudSync(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var baseUrl = configuration["CloudApi:BaseUrl"] ?? "http://10.98.90.154:81";

        var localAdminConfig = new LocalAdminConfig();
        configuration.GetSection("LocalAdmin").Bind(localAdminConfig);
        services.AddSingleton(localAdminConfig);

        services.AddHttpClient<AuthService>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<IAuthService>(sp => sp.GetRequiredService<AuthService>());

        services.AddHttpClient<DeviceService>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(3);
        });
        services.AddSingleton<IDeviceService>(sp => sp.GetRequiredService<DeviceService>());

        return services;
    }
}