using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBacktesting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FeePercentPerSide",
                table: "TradingSettings",
                type: "decimal(8,4)",
                precision: 8,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "BacktestRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Symbol = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Interval = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedStartUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    RequestedEndUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ActualStartUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ActualEndUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CandleCount = table.Column<int>(type: "int", nullable: false),
                    RiskReward = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    FixedOrderSizeUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    WaitForConfirmationCandle = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UseEma100Filter = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TrailingStopEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    FeePercentPerSide = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    TotalTrades = table.Column<int>(type: "int", nullable: false),
                    WinningTrades = table.Column<int>(type: "int", nullable: false),
                    LosingTrades = table.Column<int>(type: "int", nullable: false),
                    BreakEvenTrades = table.Column<int>(type: "int", nullable: false),
                    LongTrades = table.Column<int>(type: "int", nullable: false),
                    ShortTrades = table.Column<int>(type: "int", nullable: false),
                    WinRatePercent = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    GrossPnlUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    NetPnlUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    TotalFeesUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    ProfitFactor = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    AverageNetPnlUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    AverageRMultiple = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    MaxDrawdownUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    TotalCrossovers = table.Column<int>(type: "int", nullable: false),
                    LongSignals = table.Column<int>(type: "int", nullable: false),
                    ShortSignals = table.Column<int>(type: "int", nullable: false),
                    RejectedByEma100 = table.Column<int>(type: "int", nullable: false),
                    ConfirmationFailed = table.Column<int>(type: "int", nullable: false),
                    InvalidStopLoss = table.Column<int>(type: "int", nullable: false),
                    SkippedWhilePositionOpen = table.Column<int>(type: "int", nullable: false),
                    NoEntryCandle = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FailureMessage = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestRuns", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BacktestTrades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BacktestRunId = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    CrossoverTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    SignalTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    EntryTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ExitTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    ExitPrice = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    EntryNotionalUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    InitialStopLoss = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    FinalStopLoss = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    StopSourceType = table.Column<int>(type: "int", nullable: false),
                    StopSourceTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    OriginalTakeProfit = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    FinalTakeProfit = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    TakeProfitExtended = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExitReason = table.Column<int>(type: "int", nullable: false),
                    SameCandleExitConflict = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EntryFeeUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    ExitFeeUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    TotalFeesUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    GrossPnlUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    NetPnlUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    NetPnlPercent = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    GrossRMultiple = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    NetRMultiple = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    MfePrice = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    MfePercent = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    MaePrice = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    MaePercent = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    SignalClose = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    SignalEma9 = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    SignalEma15 = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    SignalEma100 = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    SignalGapPercent = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    SignalGapState = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BacktestTrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BacktestTrades_BacktestRuns_BacktestRunId",
                        column: x => x.BacktestRunId,
                        principalTable: "BacktestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BacktestTrades_BacktestRunId",
                table: "BacktestTrades",
                column: "BacktestRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BacktestTrades");

            migrationBuilder.DropTable(
                name: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "FeePercentPerSide",
                table: "TradingSettings");
        }
    }
}
