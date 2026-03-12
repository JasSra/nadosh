using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeTaskExecutionBuffering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssessmentRuns",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ToolId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TargetScope = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ScopeKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Environment = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ParametersJson = table.Column<string>(type: "text", nullable: false),
                    PolicyDecisionJson = table.Column<string>(type: "text", nullable: false),
                    ApprovalReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResultSummaryJson = table.Column<string>(type: "text", nullable: true),
                    DryRun = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentRuns", x => x.RunId);
                });

            migrationBuilder.CreateTable(
                name: "EdgeTaskExecutionRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AuthorizedTaskId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SiteId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AgentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TaskKind = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LeaseToken = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LocalQueueName = table.Column<string>(type: "text", nullable: true),
                    LocalJobReference = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true),
                    RequeueRecommended = table.Column<bool>(type: "boolean", nullable: false),
                    UploadAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastUploadError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextUploadAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EdgeTaskExecutionRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRuns_CreatedAt",
                table: "AssessmentRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRuns_RequestedBy",
                table: "AssessmentRuns",
                column: "RequestedBy");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRuns_Status",
                table: "AssessmentRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentRuns_ToolId",
                table: "AssessmentRuns",
                column: "ToolId");

            migrationBuilder.CreateIndex(
                name: "IX_EdgeTaskExecutionRecords_AuthorizedTaskId",
                table: "EdgeTaskExecutionRecords",
                column: "AuthorizedTaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EdgeTaskExecutionRecords_NextUploadAttemptAt",
                table: "EdgeTaskExecutionRecords",
                column: "NextUploadAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_EdgeTaskExecutionRecords_Status",
                table: "EdgeTaskExecutionRecords",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssessmentRuns");

            migrationBuilder.DropTable(
                name: "EdgeTaskExecutionRecords");
        }
    }
}
