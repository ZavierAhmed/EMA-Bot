using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class ActivateBrokerAwareMt5PaperTrading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PaperFixedLots",
                table: "TradingSettings",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PaperMarginPerTradePercent",
                table: "TradingSettings",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PaperPositionSizingMode",
                table: "TradingSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PaperStartingBalance",
                table: "TradingSettings",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AccountEquityAtEntry",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EntryAsk",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EntryBid",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EntrySpread",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExitAsk",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExitBid",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExitSpread",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossPnl",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Lots",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginUsed",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetPnl",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RequiredMargin",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RoundTripCommission",
                table: "PaperTrades",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrokerSymbol",
                table: "PaperSessionSymbols",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "utf8mb4_bin")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionPerLotPerSide",
                table: "PaperSessionSymbols",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ContractSize",
                table: "PaperSessionSymbols",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PointSize",
                table: "PaperSessionSymbols",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StopsLevelPoints",
                table: "PaperSessionSymbols",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TickSize",
                table: "PaperSessionSymbols",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TickValueLoss",
                table: "PaperSessionSymbols",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TickValueProfit",
                table: "PaperSessionSymbols",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TradeMode",
                table: "PaperSessionSymbols",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeLimit",
                table: "PaperSessionSymbols",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeMax",
                table: "PaperSessionSymbols",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeMin",
                table: "PaperSessionSymbols",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumeStep",
                table: "PaperSessionSymbols",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountCurrency",
                table: "PaperSessions",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "USDT")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "CurrentBalance",
                table: "PaperSessions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetPnl",
                table: "PaperSessions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PaperFixedLots",
                table: "PaperSessions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PaperMarginPerTradePercent",
                table: "PaperSessions",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PaperPositionSizingMode",
                table: "PaperSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RejectedByTradingCosts",
                table: "PaperSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "StartingBalance",
                table: "PaperSessions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalTradingCosts",
                table: "PaperSessions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UsedMargin",
                table: "PaperSessions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PaperCommissionPerLotPerSide",
                table: "MonitoredSymbols",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.Sql("UPDATE `TradingSettings` SET `PaperFixedLots` = 0.01, `PaperMarginPerTradePercent` = `MarginPerTradePercent`, `PaperStartingBalance` = `SimulatedAccountBalanceUsdt`");
            migrationBuilder.Sql("UPDATE `PaperSessions` SET `AccountCurrency` = 'USDT', `StartingBalance` = `StartingBalanceUsdt`, `CurrentBalance` = `CurrentBalanceUsdt`, `UsedMargin` = `UsedMarginUsdt`, `NetPnl` = `NetPnlUsdt`, `TotalTradingCosts` = `TotalFeesUsdt`");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaperFixedLots",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "PaperMarginPerTradePercent",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "PaperPositionSizingMode",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "PaperStartingBalance",
                table: "TradingSettings");

            migrationBuilder.DropColumn(
                name: "AccountEquityAtEntry",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "EntryAsk",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "EntryBid",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "EntrySpread",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "ExitAsk",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "ExitBid",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "ExitSpread",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "GrossPnl",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "Lots",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "MarginUsed",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "NetPnl",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "RequiredMargin",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "RoundTripCommission",
                table: "PaperTrades");

            migrationBuilder.DropColumn(
                name: "BrokerSymbol",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "CommissionPerLotPerSide",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "ContractSize",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "PointSize",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "StopsLevelPoints",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "TickSize",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "TickValueLoss",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "TickValueProfit",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "TradeMode",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "VolumeLimit",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "VolumeMax",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "VolumeMin",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "VolumeStep",
                table: "PaperSessionSymbols");

            migrationBuilder.DropColumn(
                name: "AccountCurrency",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "CurrentBalance",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "NetPnl",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "PaperFixedLots",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "PaperMarginPerTradePercent",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "PaperPositionSizingMode",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "RejectedByTradingCosts",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "StartingBalance",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "TotalTradingCosts",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "UsedMargin",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "PaperCommissionPerLotPerSide",
                table: "MonitoredSymbols");
        }
    }
}
