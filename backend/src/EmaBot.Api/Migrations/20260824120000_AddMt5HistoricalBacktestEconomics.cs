using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations;

public partial class AddMt5HistoricalBacktestEconomics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        AddRunColumns(migrationBuilder); AddTradeColumns(migrationBuilder);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var column in RunColumns) migrationBuilder.DropColumn(column, "BacktestRuns");
        foreach (var column in TradeColumns) migrationBuilder.DropColumn(column, "BacktestTrades");
    }
    private static readonly string[] RunColumns = ["EconomicsMode","AccountCurrency","BrokerSymbol","HistoricalSpreadModel","HistoricalChartMode","CommissionPerLotPerSide","ContractSize","VolumeMin","VolumeMax","VolumeStep","VolumeLimit","PointSize","TickSize","TickValueProfit","TickValueLoss","StopsLevelPoints","TradeMode","StartingBalance","EndingBalance","GrossProfitFactor","NetProfitFactor","RejectedByTradingCosts","Mt5EconomicsCallCount","Mt5EconomicsElapsedMilliseconds"];
    private static readonly string[] TradeColumns = ["Lots","EntryBid","EntryAsk","EntrySpread","ExitBid","ExitAsk","ExitSpread","RequiredMargin","MarginUsed","AccountEquityAtEntry","EntryCommission","ExitCommission","RoundTripCommission","GrossPnl","NetPnl","InitialRiskAmount","ReentryAgeBars"];
    private static void AddRunColumns(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "EconomicsMode", table: "BacktestRuns", type: "int", nullable: true);
        migrationBuilder.AddColumn<string>(name: "AccountCurrency", table: "BacktestRuns", type: "varchar(16)", maxLength: 16, nullable: true);
        migrationBuilder.AddColumn<string>(name: "BrokerSymbol", table: "BacktestRuns", type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "HistoricalSpreadModel", table: "BacktestRuns", type: "varchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "HistoricalChartMode", table: "BacktestRuns", type: "varchar(16)", maxLength: 16, nullable: true);
        migrationBuilder.AddColumn<string>(name: "TradeMode", table: "BacktestRuns", type: "varchar(16)", maxLength: 16, nullable: true);
        migrationBuilder.AddColumn<int>(name: "StopsLevelPoints", table: "BacktestRuns", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "RejectedByTradingCosts", table: "BacktestRuns", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "Mt5EconomicsCallCount", table: "BacktestRuns", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<long>(name: "Mt5EconomicsElapsedMilliseconds", table: "BacktestRuns", type: "bigint", nullable: false, defaultValue: 0L);
        foreach (var name in new[] { "CommissionPerLotPerSide","ContractSize","VolumeMin","VolumeMax","VolumeStep","VolumeLimit","PointSize","TickSize","TickValueProfit","TickValueLoss","StartingBalance","EndingBalance","GrossProfitFactor","NetProfitFactor" }) migrationBuilder.AddColumn<decimal>(name: name, table: "BacktestRuns", type: "decimal(18,8)", precision: 18, scale: 8, nullable: true);
    }
    private static void AddTradeColumns(MigrationBuilder migrationBuilder)
    {
        foreach (var name in TradeColumns.Where(name => name != "ReentryAgeBars")) migrationBuilder.AddColumn<decimal>(name: name, table: "BacktestTrades", type: "decimal(18,8)", precision: 18, scale: 8, nullable: true);
        migrationBuilder.AddColumn<int>(name: "ReentryAgeBars", table: "BacktestTrades", type: "int", nullable: true);
    }
}
