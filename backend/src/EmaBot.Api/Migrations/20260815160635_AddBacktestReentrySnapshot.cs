using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBacktestReentrySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxReentryAgeBars",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<bool>(
                name: "SameTrendReentryEnabled",
                table: "BacktestRuns",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxReentryAgeBars",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "SameTrendReentryEnabled",
                table: "BacktestRuns");
        }
    }
}
