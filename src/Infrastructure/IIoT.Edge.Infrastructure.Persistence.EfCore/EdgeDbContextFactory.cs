using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IIoT.Edge.Infrastructure.Persistence.EfCore;

public class EdgeDbContextFactory : IDesignTimeDbContextFactory<EdgeDbContext>
{
    public EdgeDbContext CreateDbContext(string[] args)
    {
        var dbPath = EdgeSqliteConnection.ResolveDesignTimeDbPath(args);
        EdgeSqliteConnection.EnsureRuntimePragmas(dbPath);

        var optionsBuilder = new DbContextOptionsBuilder<EdgeDbContext>();
        optionsBuilder.UseSqlite(EdgeSqliteConnection.BuildConnectionString(dbPath));
        return new EdgeDbContext(optionsBuilder.Options);
    }
}
