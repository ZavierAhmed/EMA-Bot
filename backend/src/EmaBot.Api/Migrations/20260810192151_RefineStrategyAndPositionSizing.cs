using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class RefineStrategyAndPositionSizing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Leverage",
                table: "TradingSettings",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 5m);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginPerTradePercent",
                table: "TradingSettings",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 10m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxStopDistancePercent",
                table: "TradingSettings",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinEmaGapPercent",
                table: "TradingSettings",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0.01m);

            migrationBuilder.AddColumn<int>(
                name: "PositionSizingMode",
                table: "TradingSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SimulatedAccountBalanceUsdt",
                table: "TradingSettings",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1000m);

            migrationBuilder.AddColumn<decimal>(
                name: "AccountEquityAtEntryUsdt",
                table: "PaperTrades",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReentry",
                table: "PaperTrades",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Leverage",
                table: "PaperTrades",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginUsedUsdt",
                table: "PaperTrades",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PositionSizingMode",
                table: "PaperTrades",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SignalOpen",
                table: "PaperTrades",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrendRegimeCrossoverTimeUtc",
                table: "PaperTrades",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PendingSignalOpen",
                table: "PaperSessionSymbols",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentBalanceUsdt",
                table: "PaperSessions",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Leverage",
                table: "PaperSessions",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginPerTradePercent",
                table: "PaperSessions",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxStopDistancePercent",
                table: "PaperSessions",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinEmaGapPercent",
                table: "PaperSessions",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PositionSizingMode",
                table: "PaperSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByEmaGap",
                table: "PaperSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByFees",
                table: "PaperSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByInsufficientMargin",
                table: "PaperSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByStopDistance",
                table: "PaperSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "StartingBalanceUsdt",
                table: "PaperSessions",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UsedMarginUsdt",
                table: "PaperSessions",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AccountEquityAtEntryUsdt",
                table: "BacktestTrades",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReentry",
                table: "BacktestTrades",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Leverage",
                table: "BacktestTrades",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginUsedUsdt",
                table: "BacktestTrades",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PositionSizingMode",
                table: "BacktestTrades",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SignalOpen",
                table: "BacktestTrades",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrendRegimeCrossoverTimeUtc",
                table: "BacktestTrades",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EndingBalanceUsdt",
                table: "BacktestRuns",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Leverage",
                table: "BacktestRuns",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginPerTradePercent",
                table: "BacktestRuns",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxStopDistancePercent",
                table: "BacktestRuns",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinEmaGapPercent",
                table: "BacktestRuns",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PositionSizingMode",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByEmaGap",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByFees",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByStopDistance",
                table: "BacktestRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "StartingBalanceUsdt",
                table: "BacktestRuns",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Leverage",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "MarginPerTradePercent",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "MaxStopDistancePercent",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "MinEmaGapPercent",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "PositionSizingMode",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "SimulatedAccountBalanceUsdt",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "AccountEquityAtEntryUsdt",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "IsReentry",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "Leverage",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "MarginUsedUsdt",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "PositionSizingMode",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "SignalOpen",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "TrendRegimeCrossoverTimeUtc",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "PendingSignalOpen",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "CurrentBalanceUsdt",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "Leverage",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "MarginPerTradePercent",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "MaxStopDistancePercent",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "MinEmaGapPercent",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "PositionSizingMode",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "RejectedByEmaGap",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "RejectedByFees",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "RejectedByInsufficientMargin",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "RejectedByStopDistance",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "StartingBalanceUsdt",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "UsedMarginUsdt",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "AccountEquityAtEntryUsdt",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "IsReentry",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "Leverage",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "MarginUsedUsdt",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "PositionSizingMode",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "SignalOpen",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "TrendRegimeCrossoverTimeUtc",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "EndingBalanceUsdt",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "Leverage",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "MarginPerTradePercent",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "MaxStopDistancePercent",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "MinEmaGapPercent",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "PositionSizingMode",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "RejectedByEmaGap",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "RejectedByFees",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "RejectedByStopDistance",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "StartingBalanceUsdt",
                table: "BacktestRuns");
        }
    }
}
