using IIoT.Edge.Infrastructure.Persistence.EfCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EdgeDbContext))]
    [Migration("20260603090000_RemoveHardwareConfigBindingFields")]
    public partial class RemoveHardwareConfigBindingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE hw_network_device DROP COLUMN module_id;");
            migrationBuilder.Sql("ALTER TABLE hw_io_mapping DROP COLUMN signal_name;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE hw_network_device ADD COLUMN module_id TEXT NOT NULL DEFAULT '';");
            migrationBuilder.Sql("ALTER TABLE hw_io_mapping ADD COLUMN signal_name TEXT NOT NULL DEFAULT '';");
        }
    }
}
