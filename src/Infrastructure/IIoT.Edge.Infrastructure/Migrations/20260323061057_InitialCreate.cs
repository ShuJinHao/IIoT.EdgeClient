using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.Edge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hw_network_device",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    device_name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    device_type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ip_address = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    port1 = table.Column<int>(type: "INTEGER", nullable: false),
                    port2 = table.Column<int>(type: "INTEGER", nullable: true),
                    send_cmd1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    send_cmd2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    connect_timeout = table.Column<int>(type: "INTEGER", nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    remark = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hw_network_device", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hw_serial_device",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    device_name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    device_type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    port_name = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    baud_rate = table.Column<int>(type: "INTEGER", nullable: false),
                    data_bits = table.Column<int>(type: "INTEGER", nullable: false),
                    stop_bits = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    parity = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    send_cmd1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    send_cmd2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    remark = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hw_serial_device", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hw_io_mapping",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    network_device_id = table.Column<int>(type: "INTEGER", nullable: false),
                    label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    plc_address = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    address_count = table.Column<int>(type: "INTEGER", nullable: false),
                    data_type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    direction = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false),
                    remark = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hw_io_mapping", x => x.id);
                    table.ForeignKey(
                        name: "FK_hw_io_mapping_hw_network_device_network_device_id",
                        column: x => x.network_device_id,
                        principalTable: "hw_network_device",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hw_io_mapping_network_device_id",
                table: "hw_io_mapping",
                column: "network_device_id");

            migrationBuilder.CreateIndex(
                name: "ix_hw_network_device_ip",
                table: "hw_network_device",
                column: "ip_address");

            migrationBuilder.CreateIndex(
                name: "ix_hw_serial_device_port_name",
                table: "hw_serial_device",
                column: "port_name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hw_io_mapping");

            migrationBuilder.DropTable(
                name: "hw_serial_device");

            migrationBuilder.DropTable(
                name: "hw_network_device");
        }
    }
}
