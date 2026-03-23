using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

namespace IIoT.Edge.Infrastructure;

public class EdgeDbContextFactory : IDesignTimeDbContextFactory<EdgeDbContext>
{
    public EdgeDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EdgeDbContext>();
        optionsBuilder.UseSqlite("Data Source=edge_design.db");
        return new EdgeDbContext(optionsBuilder.Options);
    }
}