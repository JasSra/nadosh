using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    OldValueJson = table.Column<string>(type: "jsonb", nullable: true),
                    NewValueJson = table.Column<string>(type: "jsonb", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CertificateObservations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TargetId = table.Column<string>(type: "text", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    SanList = table.Column<List<string>>(type: "text[]", nullable: false),
                    Issuer = table.Column<string>(type: "text", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateObservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CurrentExposures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TargetId = table.Column<string>(type: "text", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Protocol = table.Column<string>(type: "text", nullable: false),
                    CurrentState = table.Column<string>(type: "text", nullable: false),
                    FirstSeen = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastChanged = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Classification = table.Column<string>(type: "text", nullable: true),
                    Severity = table.Column<string>(type: "text", nullable: true),
                    CachedSummary = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrentExposures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnrichmentResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ObservationId = table.Column<long>(type: "bigint", nullable: true),
                    CurrentExposureId = table.Column<long>(type: "bigint", nullable: true),
                    RuleId = table.Column<string>(type: "text", nullable: false),
                    RuleVersion = table.Column<string>(type: "text", nullable: false),
                    ResultStatus = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: true),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrichmentResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Observations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TargetId = table.Column<string>(type: "text", nullable: false),
                    ObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Protocol = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: true),
                    Fingerprint = table.Column<string>(type: "text", nullable: true),
                    EvidenceJson = table.Column<string>(type: "jsonb", nullable: true),
                    ScanRunId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Observations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuleConfigs",
                columns: table => new
                {
                    RuleId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    ServiceType = table.Column<string>(type: "text", nullable: false),
                    TriggerConditionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    RequestDefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    MatcherDefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    SeverityMappingJson = table.Column<string>(type: "jsonb", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleConfigs", x => new { x.RuleId, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "ScanRuns",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "text", nullable: false),
                    Stage = table.Column<string>(type: "text", nullable: false),
                    Shard = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkerId = table.Column<string>(type: "text", nullable: false),
                    CountsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanRuns", x => x.RunId);
                });

            migrationBuilder.CreateTable(
                name: "SuppressionRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TargetIp = table.Column<string>(type: "text", nullable: true),
                    Cidr = table.Column<string>(type: "text", nullable: true),
                    Port = table.Column<int>(type: "integer", nullable: true),
                    ServiceType = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    Creator = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuppressionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Targets",
                columns: table => new
                {
                    Ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    CidrSource = table.Column<string>(type: "text", nullable: true),
                    OwnershipTags = table.Column<List<string>>(type: "text[]", nullable: false),
                    Monitored = table.Column<bool>(type: "boolean", nullable: false),
                    LastScheduled = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextScheduled = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Targets", x => x.Ip);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CertificateObservations_Sha256",
                table: "CertificateObservations",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateObservations_Subject",
                table: "CertificateObservations",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_CurrentExposures_TargetId_Port_Protocol",
                table: "CurrentExposures",
                columns: new[] { "TargetId", "Port", "Protocol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentResults_CurrentExposureId",
                table: "EnrichmentResults",
                column: "CurrentExposureId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrichmentResults_ObservationId",
                table: "EnrichmentResults",
                column: "ObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_ObservedAt",
                table: "Observations",
                column: "ObservedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Observations_TargetId",
                table: "Observations",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_SuppressionRules_TargetIp",
                table: "SuppressionRules",
                column: "TargetIp");

            migrationBuilder.CreateIndex(
                name: "IX_Targets_NextScheduled",
                table: "Targets",
                column: "NextScheduled");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "CertificateObservations");

            migrationBuilder.DropTable(
                name: "CurrentExposures");

            migrationBuilder.DropTable(
                name: "EnrichmentResults");

            migrationBuilder.DropTable(
                name: "Observations");

            migrationBuilder.DropTable(
                name: "RuleConfigs");

            migrationBuilder.DropTable(
                name: "ScanRuns");

            migrationBuilder.DropTable(
                name: "SuppressionRules");

            migrationBuilder.DropTable(
                name: "Targets");
        }
    }
}
