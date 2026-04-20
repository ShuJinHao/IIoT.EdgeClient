using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    public partial class AddNetworkDeviceModuleId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "module_id",
                table: "hw_network_device",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "module_id",
                table: "hw_network_device");
        }
    }
}
