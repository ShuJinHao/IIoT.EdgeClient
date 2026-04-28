using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Infrastructure.Persistence.EfCore.Caching.Memory;
using IIoT.Edge.Infrastructure.Persistence.EfCore.Repositories;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore;

public static class DependencyInjection
{
    public static IServiceCollection AddEfCorePersistenceInfrastructure(
        this IServiceCollection services,
        string dbPath)
    {
        EdgeSqliteConnection.EnsureRuntimePragmas(dbPath);

        services.AddDbContextFactory<EdgeDbContext>(
            options => options.UseSqlite(EdgeSqliteConnection.BuildConnectionString(dbPath)));

        services.AddSingleton(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        services.AddSingleton(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddSingleton<IEdgeCacheService, EdgeMemoryCacheService>();

        return services;
    }

    public static void ApplyMigrations(this IServiceProvider serviceProvider)
    {
        var factory = serviceProvider.GetRequiredService<IDbContextFactory<EdgeDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.Migrate();
    }
}
