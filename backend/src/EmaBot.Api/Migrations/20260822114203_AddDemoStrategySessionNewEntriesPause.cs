using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoStrategySessionNewEntriesPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NewEntriesPaused",
                table: "DemoStrategySessions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NewEntriesPausedAtUtc",
                table: "DemoStrategySessions",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewEntriesPaused",
                table: "DemoStrategySessions");

            migrationBuilder.DropColumn(
                name: "NewEntriesPausedAtUtc",
                table: "DemoStrategySessions");
        }
    }
}
