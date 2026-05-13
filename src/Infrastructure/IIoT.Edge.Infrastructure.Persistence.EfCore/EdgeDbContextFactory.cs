using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore;

public class EdgeDbContextFactory : IDesignTimeDbContextFactory<EdgeDbContext>
{
    public EdgeDbContext CreateDbContext(string[] args)
    {
        var sqliteConnection = new EdgeSqliteConnection();
        var dbPath = sqliteConnection.ResolveDesignTimeDbPath(args);
        sqliteConnection.EnsureRuntimePragmas(dbPath);

        var optionsBuilder = new DbContextOptionsBuilder<EdgeDbContext>();
        optionsBuilder.UseSqlite(sqliteConnection.BuildConnectionString(dbPath));
        return new EdgeDbContext(optionsBuilder.Options);
    }
}
