using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOptionalHtfRegimeFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseHtfRegimeFilter",
                table: "TradingSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UseHtfRegimeFilter",
                table: "StrategyOptimizationCandidates",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "HtfTimeframe",
                table: "BacktestTrades",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "SignalHtfAtr14Percent",
                table: "BacktestTrades",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignalHtfCandleCloseTimeUtc",
                table: "BacktestTrades",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SignalHtfEma100Slope20Percent",
                table: "BacktestTrades",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByHtfRegime",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "UseHtfRegimeFilter",
                table: "BacktestRuns",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseHtfRegimeFilter",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "UseHtfRegimeFilter",
                table: "StrategyOptimizationCandidates");

            migrationBuilder.DropColumn(
                name: "HtfTimeframe",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "SignalHtfAtr14Percent",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "SignalHtfCandleCloseTimeUtc",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "SignalHtfEma100Slope20Percent",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "RejectedByHtfRegime",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "UseHtfRegimeFilter",
                table: "BacktestRuns");
        }
    }
}
