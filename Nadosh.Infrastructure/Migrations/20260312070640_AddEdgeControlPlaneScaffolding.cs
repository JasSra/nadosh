using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeControlPlaneScaffolding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EdgeSites",
                columns: table => new
                {
                    SiteId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ApprovalScopeJson = table.Column<string>(type: "text", nullable: false),
                    DataHandlingPolicyJson = table.Column<string>(type: "text", nullable: false),
                    AllowedCidrs = table.Column<List<string>>(type: "text[]", nullable: false),
                    AllowedCapabilities = table.Column<List<string>>(type: "text[]", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EdgeSites", x => x.SiteId);
                });

            migrationBuilder.CreateTable(
                name: "EdgeAgents",
                columns: table => new
                {
                    AgentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SiteId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Hostname = table.Column<string>(type: "text", nullable: false),
                    OperatingSystem = table.Column<string>(type: "text", nullable: false),
                    Architecture = table.Column<string>(type: "text", nullable: false),
                    AgentVersion = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AdvertisedCapabilities = table.Column<List<string>>(type: "text[]", nullable: false),
                    EnrolledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastHeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastKnownAddress = table.Column<string>(type: "text", nullable: true),
                    MetadataJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EdgeAgents", x => x.AgentId);
                    table.ForeignKey(
                        name: "FK_EdgeAgents_EdgeSites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "EdgeSites",
                        principalColumn: "SiteId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuthorizedTasks",
                columns: table => new
                {
                    TaskId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SiteId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AgentId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TaskKind = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    ScopeJson = table.Column<string>(type: "text", nullable: false),
                    RequiredCapabilities = table.Column<List<string>>(type: "text[]", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseToken = table.Column<string>(type: "text", nullable: true),
                    IssuedBy = table.Column<string>(type: "text", nullable: true),
                    ApprovalReference = table.Column<string>(type: "text", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NotBefore = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultSummaryJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizedTasks", x => x.TaskId);
                    table.ForeignKey(
                        name: "FK_AuthorizedTasks_EdgeAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "EdgeAgents",
                        principalColumn: "AgentId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AuthorizedTasks_EdgeSites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "EdgeSites",
                        principalColumn: "SiteId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizedTasks_AgentId",
                table: "AuthorizedTasks",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizedTasks_ExpiresAt",
                table: "AuthorizedTasks",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizedTasks_SiteId_Status",
                table: "AuthorizedTasks",
                columns: new[] { "SiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EdgeAgents_LastSeenAt",
                table: "EdgeAgents",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_EdgeAgents_SiteId",
                table: "EdgeAgents",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_EdgeAgents_Status",
                table: "EdgeAgents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EdgeSites_Name",
                table: "EdgeSites",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthorizedTasks");

            migrationBuilder.DropTable(
                name: "EdgeAgents");

            migrationBuilder.DropTable(
                name: "EdgeSites");
        }
    }
}
