using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeBacktestRejectionDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RejectedByInsufficientMargin",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByInvalidVolume",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByTradeMode",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectedByInsufficientMargin",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "RejectedByInvalidVolume",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "RejectedByTradeMode",
                table: "BacktestRuns");
        }
    }
}
