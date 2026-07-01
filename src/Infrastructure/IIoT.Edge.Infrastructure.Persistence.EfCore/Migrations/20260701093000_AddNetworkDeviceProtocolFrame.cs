using IIoT.Edge.Infrastructure.Persistence.EfCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EdgeDbContext))]
    [Migration("20260701093000_AddNetworkDeviceProtocolFrame")]
    public partial class AddNetworkDeviceProtocolFrame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "protocol_frame",
                table: "hw_network_device",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "protocol_frame",
                table: "hw_network_device");
        }
    }
}
