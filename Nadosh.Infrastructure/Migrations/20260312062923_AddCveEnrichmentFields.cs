using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCveEnrichmentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CveIds",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CveSeverity",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HighestCvssScore",
                table: "Observations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CveIds",
                table: "CurrentExposures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CveLastChecked",
                table: "CurrentExposures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CveSeverity",
                table: "CurrentExposures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HighestCvssScore",
                table: "CurrentExposures",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CveIds",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "CveSeverity",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "HighestCvssScore",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "CveIds",
                table: "CurrentExposures");

            migrationBuilder.DropColumn(
                name: "CveLastChecked",
                table: "CurrentExposures");

            migrationBuilder.DropColumn(
                name: "CveSeverity",
                table: "CurrentExposures");

            migrationBuilder.DropColumn(
                name: "HighestCvssScore",
                table: "CurrentExposures");
        }
    }
}
