using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEdgeTaskLeaseRenewalRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimedByAgentId",
                table: "AuthorizedTasks",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "AuthorizedTasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizedTasks_ClaimedByAgentId",
                table: "AuthorizedTasks",
                column: "ClaimedByAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizedTasks_LeaseExpiresAt",
                table: "AuthorizedTasks",
                column: "LeaseExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuthorizedTasks_ClaimedByAgentId",
                table: "AuthorizedTasks");

            migrationBuilder.DropIndex(
                name: "IX_AuthorizedTasks_LeaseExpiresAt",
                table: "AuthorizedTasks");

            migrationBuilder.DropColumn(
                name: "ClaimedByAgentId",
                table: "AuthorizedTasks");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "AuthorizedTasks");
        }
    }
}
