using IIoT.Edge.Domain.Hardware.Aggregates;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.Migrations;

[DbContext(typeof(EdgeDbContext))]
[Migration("20260710103000_AddStableNetworkDevicePlcCode")]
public partial class AddStableNetworkDevicePlcCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "plc_code",
            table: "hw_network_device",
            type: "TEXT",
            maxLength: NetworkDeviceEntity.PlcCodeMaxLength,
            nullable: false,
            defaultValue: "");

        migrationBuilder.Sql(
            """
            UPDATE hw_network_device
            SET plc_code = CASE
                WHEN LENGTH(TRIM(device_name)) BETWEEN 1 AND 64
                     AND UPPER(TRIM(device_name)) NOT LIKE 'PLC-INTERNAL-%'
                     AND (
                         SELECT COUNT(*)
                         FROM hw_network_device AS candidate
                         WHERE UPPER(TRIM(candidate.device_name)) = UPPER(TRIM(hw_network_device.device_name))
                     ) = 1
                    THEN UPPER(TRIM(device_name))
                ELSE 'PLC-INTERNAL-' || id
            END;
            """);

        migrationBuilder.CreateIndex(
            name: "ux_hw_network_device_plc_code",
            table: "hw_network_device",
            column: "plc_code",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_hw_network_device_plc_code",
            table: "hw_network_device");

        migrationBuilder.DropColumn(
            name: "plc_code",
            table: "hw_network_device");
    }
}
