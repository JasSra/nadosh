using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddObservationHandoffDispatchTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ObservationHandoffDispatches",
                columns: table => new
                {
                    DispatchKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceObservationId = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BatchId = table.Column<string>(type: "text", nullable: false),
                    TargetIp = table.Column<string>(type: "text", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Protocol = table.Column<string>(type: "text", nullable: false),
                    ServiceName = table.Column<string>(type: "text", nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WorkerId = table.Column<string>(type: "text", nullable: true),
                    DeliveryCount = table.Column<int>(type: "integer", nullable: false),
                    ProducedObservationId = table.Column<long>(type: "bigint", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObservationHandoffDispatches", x => new { x.DispatchKind, x.SourceObservationId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ObservationHandoffDispatches_ProducedObservationId",
                table: "ObservationHandoffDispatches",
                column: "ProducedObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_ObservationHandoffDispatches_ScheduledAt",
                table: "ObservationHandoffDispatches",
                column: "ScheduledAt");

            migrationBuilder.CreateIndex(
                name: "IX_ObservationHandoffDispatches_State",
                table: "ObservationHandoffDispatches",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ObservationHandoffDispatches");
        }
    }
}
