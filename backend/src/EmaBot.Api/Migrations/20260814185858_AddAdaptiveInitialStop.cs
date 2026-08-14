using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdaptiveInitialStop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseAdaptiveInitialStop",
                table: "TradingSettings",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReversalPowerBand",
                table: "PaperTrades",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReversalPowerScore",
                table: "PaperTrades",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SignalAtr14",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StopAnchorPrice",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StopBuffer",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseAdaptiveInitialStop",
                table: "PaperTrades",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PendingReversalPowerBand",
                table: "PaperSessionSymbols",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PendingReversalPowerScore",
                table: "PaperSessionSymbols",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PendingSignalAtr14",
                table: "PaperSessionSymbols",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PendingStopAnchorPrice",
                table: "PaperSessionSymbols",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PendingStopBuffer",
                table: "PaperSessionSymbols",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseAdaptiveInitialStop",
                table: "PaperSessions",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReversalPowerBand",
                table: "BacktestTrades",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReversalPowerScore",
                table: "BacktestTrades",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SignalAtr14",
                table: "BacktestTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StopAnchorPrice",
                table: "BacktestTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StopBuffer",
                table: "BacktestTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseAdaptiveInitialStop",
                table: "BacktestTrades",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UseAdaptiveInitialStop",
                table: "BacktestRuns",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseAdaptiveInitialStop",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "ReversalPowerBand",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "ReversalPowerScore",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "SignalAtr14",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "StopAnchorPrice",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "StopBuffer",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "UseAdaptiveInitialStop",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "PendingReversalPowerBand",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "PendingReversalPowerScore",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "PendingSignalAtr14",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "PendingStopAnchorPrice",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "PendingStopBuffer",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "UseAdaptiveInitialStop",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "ReversalPowerBand",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "ReversalPowerScore",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "SignalAtr14",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "StopAnchorPrice",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "StopBuffer",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "UseAdaptiveInitialStop",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "UseAdaptiveInitialStop",
                table: "BacktestRuns");
        }
    }
}
