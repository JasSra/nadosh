using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStage1DispatchTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stage1Dispatches",
                columns: table => new
                {
                    BatchId = table.Column<string>(type: "text", nullable: false),
                    TargetIp = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkerId = table.Column<string>(type: "text", nullable: true),
                    DeliveryCount = table.Column<int>(type: "integer", nullable: false),
                    PortsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ObservationLinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ObservationCount = table.Column<int>(type: "integer", nullable: false),
                    OpenObservationCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stage1Dispatches", x => new { x.BatchId, x.TargetIp });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stage1Dispatches_ScheduledAt",
                table: "Stage1Dispatches",
                column: "ScheduledAt");

            migrationBuilder.CreateIndex(
                name: "IX_Stage1Dispatches_State",
                table: "Stage1Dispatches",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Stage1Dispatches");
        }
    }
}
