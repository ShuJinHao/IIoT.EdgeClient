using IIoT.Edge.Infrastructure.Persistence.Dapper.Connection;
using IIoT.Edge.Infrastructure.Persistence.Dapper.Repository;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace IIoT.Edge.Infrastructure.Persistence.Dapper;

public static class DependencyInjection
{
    /// <summary>
    /// 注册 Dapper 基础设施
    /// 
    /// 自动扫描当前程序集中所有继承 DapperRepositoryBase 的 Store
    /// 自动注册为：具体类 + 接口 + ITableInitializer
    /// 按约定新增 Store 时，只需提供实现类和接口即可被自动注册。
    /// </summary>
    public static IServiceCollection AddDapperPersistenceInfrastructure(
        this IServiceCollection services,
        string dbDirectory)
    {
        // 连接工厂
        services.AddSingleton(new SqliteConnectionFactory(dbDirectory));

        // 自动扫描注册所有 Store
        var assembly = Assembly.GetExecutingAssembly();
        var storeTypes = assembly.GetTypes()
            .Where(t => !t.IsAbstract
                && DerivesFromDapperRepositoryBase(t))
            .ToList();

        foreach (var storeType in storeTypes)
        {
            // 注册具体类（单例）
            services.AddSingleton(storeType);

            // 注册 ITableInitializer（建表用）
            services.AddSingleton<ITableInitializer>(sp =>
                (ITableInitializer)sp.GetRequiredService(storeType));

            // 扫描该类实现的业务接口（排除 ITableInitializer）
            var interfaces = storeType.GetInterfaces()
                .Where(i => i != typeof(ITableInitializer)
                    && !i.IsGenericType
                    && i.Assembly != typeof(ITableInitializer).Assembly)
                .ToList();

            foreach (var iface in interfaces)
            {
                services.AddSingleton(iface, sp => sp.GetRequiredService(storeType));
            }
        }

        return services;
    }

    private static bool DerivesFromDapperRepositoryBase(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(DapperRepositoryBase<>))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 容器构建后调用：初始化所有已注册的表
    /// </summary>
    public static async Task InitializeDapperTablesAsync(
        this IServiceProvider serviceProvider)
    {
        var factory = serviceProvider.GetRequiredService<SqliteConnectionFactory>();
        var initializers = serviceProvider.GetServices<ITableInitializer>();

        var groups = initializers.GroupBy(x => x.DbName);

        foreach (var group in groups)
        {
            using var connection = factory.Create(group.Key);

            foreach (var initializer in group)
            {
                await initializer.InitializeTableAsync(connection);
            }
        }
    }
}
