using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    HttpStatusCode = table.Column<int>(type: "integer", nullable: true),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_EventType",
                table: "WebhookDeliveries",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_SentAt",
                table: "WebhookDeliveries",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_Success",
                table: "WebhookDeliveries",
                column: "Success");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookDeliveries");
        }
    }
}
