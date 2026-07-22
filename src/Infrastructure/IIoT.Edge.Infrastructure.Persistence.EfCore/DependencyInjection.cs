using IIoT.Edge.Module.Contracts.Cache;
using IIoT.Edge.Application.Common.Caching.Memory;
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
        var sqliteConnection = new EdgeSqliteConnection();

        services.AddSingleton<IEdgeSqliteConnection>(sqliteConnection);
        services.AddSingleton(new EdgeSqliteDatabasePath(Path.GetFullPath(dbPath)));
        services.AddDbContextFactory<EdgeDbContext>(
            options => options.UseSqlite(sqliteConnection.BuildConnectionString(dbPath)));

        services.AddSingleton(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        services.AddSingleton<IEdgeUnitOfWorkFactory, EdgeUnitOfWorkFactory>();
        services.AddSingleton<IEdgeCacheService, EdgeMemoryCacheService>();
        services.AddSingleton<IEdgeSqliteSchemaRepair, EdgeSqliteSchemaRepair>();

        return services;
    }

    public static void ApplyMigrations(this IServiceProvider serviceProvider)
    {
        var sqliteConnection = serviceProvider.GetService<IEdgeSqliteConnection>();
        var databasePath = serviceProvider.GetService<EdgeSqliteDatabasePath>();
        if (sqliteConnection is not null && databasePath is not null)
            sqliteConnection.EnsureRuntimePragmas(databasePath.Value);

        var factory = serviceProvider.GetRequiredService<IDbContextFactory<EdgeDbContext>>();
        var schemaRepair = serviceProvider.GetRequiredService<IEdgeSqliteSchemaRepair>();
        using var db = factory.CreateDbContext();
        db.Database.Migrate();
        schemaRepair.Repair(db);
    }

    internal sealed record EdgeSqliteDatabasePath(string Value);
}
