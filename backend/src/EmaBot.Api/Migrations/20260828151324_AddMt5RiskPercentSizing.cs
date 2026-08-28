using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMt5RiskPercentSizing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PaperRiskPerTradePercent",
                table: "TradingSettings",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualInitialRiskPercent",
                table: "PaperTrades",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetRiskAmount",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetRiskPercent",
                table: "PaperTrades",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaperRiskPerTradePercent",
                table: "PaperSessions",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByRiskBelowMinimumVolume",
                table: "PaperSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualInitialRiskPercent",
                table: "BacktestTrades",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetRiskAmount",
                table: "BacktestTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetRiskPercent",
                table: "BacktestTrades",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NativeRiskPerTradePercent",
                table: "BacktestRuns",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByRiskBelowMinimumVolume",
                table: "BacktestRuns",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaperRiskPerTradePercent",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "ActualInitialRiskPercent",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "TargetRiskAmount",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "TargetRiskPercent",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "PaperRiskPerTradePercent",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "RejectedByRiskBelowMinimumVolume",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "ActualInitialRiskPercent",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "TargetRiskAmount",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "TargetRiskPercent",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "NativeRiskPerTradePercent",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "RejectedByRiskBelowMinimumVolume",
                table: "BacktestRuns");
        }
    }
}
