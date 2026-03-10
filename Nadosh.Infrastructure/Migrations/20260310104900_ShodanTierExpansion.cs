using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShodanTierExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AsnInfo",
                table: "Targets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Cadence",
                table: "Targets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GeoCountry",
                table: "Targets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "InterestScore",
                table: "Targets",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastStateChange",
                table: "Targets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpenPortCount",
                table: "Targets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReverseDns",
                table: "Targets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StateChangeCount",
                table: "Targets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Banner",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HttpServer",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HttpStatusCode",
                table: "Observations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HttpTitle",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JarmHash",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductVendor",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceName",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceVersion",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SslExpiry",
                table: "Observations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SslIssuer",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SslSubject",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Tier",
                table: "Observations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DaysUntilExpiry",
                table: "CertificateObservations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsExpired",
                table: "CertificateObservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSelfSigned",
                table: "CertificateObservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "KeySize",
                table: "CertificateObservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "CertificateObservations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureAlgorithm",
                table: "CertificateObservations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AsnInfo",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "Cadence",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "GeoCountry",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "InterestScore",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "LastStateChange",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "OpenPortCount",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "ReverseDns",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "StateChangeCount",
                table: "Targets");

            migrationBuilder.DropColumn(
                name: "Banner",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "HttpServer",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "HttpStatusCode",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "HttpTitle",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "JarmHash",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "ProductVendor",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "ServiceName",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "ServiceVersion",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "SslExpiry",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "SslIssuer",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "SslSubject",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "Tier",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "DaysUntilExpiry",
                table: "CertificateObservations");

            migrationBuilder.DropColumn(
                name: "IsExpired",
                table: "CertificateObservations");

            migrationBuilder.DropColumn(
                name: "IsSelfSigned",
                table: "CertificateObservations");

            migrationBuilder.DropColumn(
                name: "KeySize",
                table: "CertificateObservations");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "CertificateObservations");

            migrationBuilder.DropColumn(
                name: "SignatureAlgorithm",
                table: "CertificateObservations");
        }
    }
}
