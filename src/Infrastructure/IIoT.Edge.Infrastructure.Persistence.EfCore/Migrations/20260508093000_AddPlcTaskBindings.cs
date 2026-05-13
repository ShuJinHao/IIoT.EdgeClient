using IIoT.Edge.Infrastructure.Persistence.EfCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EdgeDbContext))]
    [Migration("20260508093000_AddPlcTaskBindings")]
    public partial class AddPlcTaskBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hw_plc_task_binding",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    network_device_id = table.Column<int>(type: "INTEGER", nullable: false),
                    task_key = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hw_plc_task_binding", x => x.id);
                    table.ForeignKey(
                        name: "FK_hw_plc_task_binding_hw_network_device_network_device_id",
                        column: x => x.network_device_id,
                        principalTable: "hw_network_device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_hw_plc_task_binding_device_task",
                table: "hw_plc_task_binding",
                columns: new[] { "network_device_id", "task_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hw_plc_task_binding");
        }
    }
}
