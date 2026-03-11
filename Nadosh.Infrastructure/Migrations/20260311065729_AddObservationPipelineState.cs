using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nadosh.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddObservationPipelineState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PipelineRetryCount",
                table: "Observations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PipelineState",
                table: "Observations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PipelineStateChangedAt",
                table: "Observations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PipelineWorkerId",
                table: "Observations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Observations_PipelineState",
                table: "Observations",
                column: "PipelineState");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Observations_PipelineState",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "PipelineRetryCount",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "PipelineState",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "PipelineStateChangedAt",
                table: "Observations");

            migrationBuilder.DropColumn(
                name: "PipelineWorkerId",
                table: "Observations");
        }
    }
}
