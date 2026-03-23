using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.Edge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DeviceModel",
                table: "hw_network_device",
                newName: "device_model");

            migrationBuilder.AlterColumn<string>(
                name: "device_type",
                table: "hw_network_device",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldMaxLength: 50);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "device_model",
                table: "hw_network_device",
                newName: "DeviceModel");

            migrationBuilder.AlterColumn<int>(
                name: "device_type",
                table: "hw_network_device",
                type: "INTEGER",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 20);
        }
    }
}
