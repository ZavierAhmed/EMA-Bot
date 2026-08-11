using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStrategyOptimizationResearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StrategyOptimizationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    FailureMessage = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedStartUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    RequestedEndUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    SymbolsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TimeframesJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GridJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BaselineSettingsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CandidateCount = table.Column<int>(type: "int", nullable: false),
                    MarketCount = table.Column<int>(type: "int", nullable: false),
                    TotalWork = table.Column<int>(type: "int", nullable: false),
                    CompletedWork = table.Column<int>(type: "int", nullable: false),
                    SimulatedAccountBalanceUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    FixedOrderSizeUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    MarginPerTradePercent = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    Leverage = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    FeePercentPerSide = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    PositionSizingMode = table.Column<int>(type: "int", nullable: false),
                    RecommendedCandidateId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyOptimizationRuns", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StrategyOptimizationCandidates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StrategyOptimizationRunId = table.Column<int>(type: "int", nullable: false),
                    RiskReward = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    MinEmaGapPercent = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    MaxStopDistancePercent = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    WaitForConfirmationCandle = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UseEma100Filter = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TrailingStopEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsBaseline = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RobustCandidate = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RobustRank = table.Column<int>(type: "int", nullable: true),
                    ProfitableMarketRatio = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    Full = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Development = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Validation = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyOptimizationCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyOptimizationCandidates_StrategyOptimizationRuns_Stra~",
                        column: x => x.StrategyOptimizationRunId,
                        principalTable: "StrategyOptimizationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StrategyOptimizationTrades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StrategyOptimizationRunId = table.Column<int>(type: "int", nullable: false),
                    StrategyOptimizationCandidateId = table.Column<int>(type: "int", nullable: false),
                    Symbol = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timeframe = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    IsReentry = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    EntryTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ExitTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    ExitPrice = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    InitialStopLoss = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    FinalStopLoss = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    OriginalTakeProfit = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    FinalTakeProfit = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    GrossPnlUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    TotalFeesUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    NetPnlUsdt = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    NetRMultiple = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    ExitReason = table.Column<int>(type: "int", nullable: false),
                    SignalEma9 = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    SignalEma15 = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    SignalEma100 = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    SignalGapPercent = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    ExpectedNetTargetR = table.Column<decimal>(type: "decimal(65,30)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyOptimizationTrades", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyOptimizationTrades_StrategyOptimizationRuns_Strategy~",
                        column: x => x.StrategyOptimizationRunId,
                        principalTable: "StrategyOptimizationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StrategyOptimizationMarketResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StrategyOptimizationCandidateId = table.Column<int>(type: "int", nullable: false),
                    Symbol = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Timeframe = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Full = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Development = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Validation = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrategyOptimizationMarketResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrategyOptimizationMarketResults_StrategyOptimizationCandid~",
                        column: x => x.StrategyOptimizationCandidateId,
                        principalTable: "StrategyOptimizationCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyOptimizationCandidates_StrategyOptimizationRunId_Rob~",
                table: "StrategyOptimizationCandidates",
                columns: new[] { "StrategyOptimizationRunId", "RobustRank" });

            migrationBuilder.CreateIndex(
                name: "IX_StrategyOptimizationMarketResults_StrategyOptimizationCandid~",
                table: "StrategyOptimizationMarketResults",
                columns: new[] { "StrategyOptimizationCandidateId", "Symbol", "Timeframe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrategyOptimizationRuns_Status",
                table: "StrategyOptimizationRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_StrategyOptimizationTrades_StrategyOptimizationRunId_Strateg~",
                table: "StrategyOptimizationTrades",
                columns: new[] { "StrategyOptimizationRunId", "StrategyOptimizationCandidateId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StrategyOptimizationMarketResults");

            migrationBuilder.DropTable(
                name: "StrategyOptimizationTrades");

            migrationBuilder.DropTable(
                name: "StrategyOptimizationCandidates");

            migrationBuilder.DropTable(
                name: "StrategyOptimizationRuns");
        }
    }
}
