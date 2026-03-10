using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMacToCurrentExposures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceType",
                table: "CurrentExposures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MacAddress",
                table: "CurrentExposures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MacVendor",
                table: "CurrentExposures",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceType",
                table: "CurrentExposures");

            migrationBuilder.DropColumn(
                name: "MacAddress",
                table: "CurrentExposures");

            migrationBuilder.DropColumn(
                name: "MacVendor",
                table: "CurrentExposures");
        }
    }
}
