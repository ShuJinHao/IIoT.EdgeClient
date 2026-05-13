using IIoT.Edge.Infrastructure.Persistence.EfCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IIoT.Edge.Infrastructure.Persistence.EfCore.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(EdgeDbContext))]
    [Migration("20260423143000_AddIoMappingDisplayFields")]
    public partial class AddIoMappingDisplayFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "hw_io_mapping",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "单点读数据");

            migrationBuilder.AddColumn<string>(
                name: "signal_name",
                table: "hw_io_mapping",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "business_group",
                table: "hw_io_mapping",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category",
                table: "hw_io_mapping");

            migrationBuilder.DropColumn(
                name: "signal_name",
                table: "hw_io_mapping");

            migrationBuilder.DropColumn(
                name: "business_group",
                table: "hw_io_mapping");
        }
    }
}
