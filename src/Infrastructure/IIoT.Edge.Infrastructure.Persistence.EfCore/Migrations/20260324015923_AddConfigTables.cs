using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cfg_device_param",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    network_device_id = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    min_value = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    max_value = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cfg_device_param", x => x.id);
                    table.ForeignKey(
                        name: "FK_cfg_device_param_hw_network_device_network_device_id",
                        column: x => x.network_device_id,
                        principalTable: "hw_network_device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cfg_system_config",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cfg_system_config", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cfg_device_param_device_id",
                table: "cfg_device_param",
                column: "network_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_cfg_system_config_key",
                table: "cfg_system_config",
                column: "key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cfg_device_param");

            migrationBuilder.DropTable(
                name: "cfg_system_config");
        }
    }
}
