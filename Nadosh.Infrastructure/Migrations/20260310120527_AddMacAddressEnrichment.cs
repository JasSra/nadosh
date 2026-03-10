using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMacAddressEnrichment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AsnNumber",
                table: "Targets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AsnOrganization",
                table: "Targets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Targets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Targets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataCenter",
                table: "Targets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceType",
                table: "Targets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EnrichmentCompletedAt",
                table: "Targets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IspName",
                table: "Targets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Targets",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Targets",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MacAddress",
                table: "Targets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MacEnrichmentCompletedAt",
                table: "Targets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MacVendor",
                table: "Targets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "Targets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceType",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MacAddress",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MacVendor",
                table: "Observations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AsnNumber",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "AsnOrganization",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "DataCenter",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "DeviceType",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "EnrichmentCompletedAt",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "IspName",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "MacAddress",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "MacEnrichmentCompletedAt",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "MacVendor",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "DeviceType",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "MacAddress",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "MacVendor",
                table: "Observations");
        }
    }
}
