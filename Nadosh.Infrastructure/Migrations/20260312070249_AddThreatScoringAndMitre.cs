using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddThreatScoringAndMitre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PortsJson",
                table: "Stage1Dispatches",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "CountsJson",
                table: "ScanRuns",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "TriggerConditionsJson",
                table: "RuleConfigs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "SeverityMappingJson",
                table: "RuleConfigs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "RequestDefinitionJson",
                table: "RuleConfigs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "MatcherDefinitionJson",
                table: "RuleConfigs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "EvidenceJson",
                table: "Observations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EvidenceJson",
                table: "EnrichmentResults",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MitreTactics",
                table: "CurrentExposures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MitreTechniques",
                table: "CurrentExposures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThreatExplanation",
                table: "CurrentExposures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThreatLevel",
                table: "CurrentExposures",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ThreatScore",
                table: "CurrentExposures",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ThreatScoreCalculatedAt",
                table: "CurrentExposures",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OldValueJson",
                table: "AuditEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NewValueJson",
                table: "AuditEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MetadataJson",
                table: "AuditEvents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MitreTactics",
                table: "CurrentExposures");

            migrationBuilder.DropColumn(
                name: "MitreTechniques",
                table: "CurrentExposures");

            migrationBuilder.DropColumn(
                name: "ThreatExplanation",
                table: "CurrentExposures");

            migrationBuilder.DropColumn(
                name: "ThreatLevel",
                table: "CurrentExposures");

            migrationBuilder.DropColumn(
                name: "ThreatScore",
                table: "CurrentExposures");

            migrationBuilder.DropColumn(
                name: "ThreatScoreCalculatedAt",
                table: "CurrentExposures");

            migrationBuilder.AlterColumn<string>(
                name: "PortsJson",
                table: "Stage1Dispatches",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CountsJson",
                table: "ScanRuns",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "TriggerConditionsJson",
                table: "RuleConfigs",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "SeverityMappingJson",
                table: "RuleConfigs",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "RequestDefinitionJson",
                table: "RuleConfigs",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "MatcherDefinitionJson",
                table: "RuleConfigs",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "EvidenceJson",
                table: "Observations",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EvidenceJson",
                table: "EnrichmentResults",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OldValueJson",
                table: "AuditEvents",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NewValueJson",
                table: "AuditEvents",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MetadataJson",
                table: "AuditEvents",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
