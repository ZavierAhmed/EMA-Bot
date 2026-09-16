using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGridHistoricalBacktests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GridBacktestRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    StrategyId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MarketDataSource = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Symbol = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BrokerSymbol = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Interval = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AccountCurrency = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HistoricalSpreadModel = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HistoricalChartMode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TradeMode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FailureMessage = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedStartUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    RequestedEndUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ActualStartUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    ActualEndUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    StartingBalance = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    EndingBalance = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    GridBasketRiskPercent = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    AdxThreshold = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    AtrSpacingMultiplier = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    CommissionPerLotPerSide = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    ContractSize = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    VolumeMin = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    VolumeMax = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    VolumeStep = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    PointSize = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    GrossPnl = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    TotalCommission = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    NetPnl = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    AverageNetPnl = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    MaxDrawdown = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    VolumeLimit = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    TickSize = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    TickValueProfit = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    TickValueLoss = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    GrossProfitFactor = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    NetProfitFactor = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    StopsLevelPoints = table.Column<int>(type: "int", nullable: true),
                    LevelCount = table.Column<int>(type: "int", nullable: false),
                    CooldownBars = table.Column<int>(type: "int", nullable: false),
                    RangeLookback = table.Column<int>(type: "int", nullable: false),
                    AtrPeriod = table.Column<int>(type: "int", nullable: false),
                    AdxPeriod = table.Column<int>(type: "int", nullable: false),
                    ReportingCandleCount = table.Column<int>(type: "int", nullable: false),
                    WarmupCandleCount = table.Column<int>(type: "int", nullable: false),
                    BasketCount = table.Column<int>(type: "int", nullable: false),
                    WinningBaskets = table.Column<int>(type: "int", nullable: false),
                    LosingBaskets = table.Column<int>(type: "int", nullable: false),
                    BreakEvenBaskets = table.Column<int>(type: "int", nullable: false),
                    LongBaskets = table.Column<int>(type: "int", nullable: false),
                    ShortBaskets = table.Column<int>(type: "int", nullable: false),
                    QualifiedCycles = table.Column<int>(type: "int", nullable: false),
                    RejectedQualificationCount = table.Column<int>(type: "int", nullable: false),
                    AmbiguousFirstSideCount = table.Column<int>(type: "int", nullable: false),
                    NoFillCyclesAtEndOfData = table.Column<int>(type: "int", nullable: false),
                    TradeModeBlockedCount = table.Column<int>(type: "int", nullable: false),
                    RiskBelowMinimumVolumeCount = table.Column<int>(type: "int", nullable: false),
                    RiskCannotBeSafelySizedCount = table.Column<int>(type: "int", nullable: false),
                    RiskCalculationUnavailableCount = table.Column<int>(type: "int", nullable: false),
                    MarginCalculationUnavailableCount = table.Column<int>(type: "int", nullable: false),
                    InsufficientMarginCount = table.Column<int>(type: "int", nullable: false),
                    EconomicsCallCount = table.Column<int>(type: "int", nullable: false),
                    EconomicsElapsedMilliseconds = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridBacktestRuns", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GridBacktestCycles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GridBacktestRunId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    QualificationTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    RangeHigh = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    RangeLow = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    QualificationClose = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    Atr = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    Adx = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    Anchor = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    Spacing = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    LongStop = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    ShortStop = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    EntryEquity = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    TargetRiskPercent = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    TargetRiskAmount = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    CommonLots = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    LongRisk = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    ShortRisk = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    LongMargin = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    ShortMargin = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    AllowedDirections = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridBacktestCycles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GridBacktestCycles_GridBacktestRuns_GridBacktestRunId",
                        column: x => x.GridBacktestRunId,
                        principalTable: "GridBacktestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GridBacktestDiagnostics",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GridBacktestRunId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    Code = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DomainCode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Operation = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BrokerSymbol = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Direction = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Detail = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Lots = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    Entry = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    Stop = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridBacktestDiagnostics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GridBacktestDiagnostics_GridBacktestRuns_GridBacktestRunId",
                        column: x => x.GridBacktestRunId,
                        principalTable: "GridBacktestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GridBacktestEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GridBacktestRunId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Time = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Type = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Level = table.Column<int>(type: "int", nullable: true),
                    ExecutablePrice = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    Detail = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridBacktestEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GridBacktestEvents_GridBacktestRuns_GridBacktestRunId",
                        column: x => x.GridBacktestRunId,
                        principalTable: "GridBacktestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GridBacktestBaskets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GridBacktestCycleId = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExitReason = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExitTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ExitPrice = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    ExitSpread = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    GrossPnl = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    Commission = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    NetPnl = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    EndingBalance = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    UsedMargin = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    ActualFilledInitialStopRisk = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    PlannedWorstCasePriceRisk = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    PlannedWorstCaseMargin = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridBacktestBaskets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GridBacktestBaskets_GridBacktestCycles_GridBacktestCycleId",
                        column: x => x.GridBacktestCycleId,
                        principalTable: "GridBacktestCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GridBacktestPlannedLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GridBacktestCycleId = table.Column<int>(type: "int", nullable: false),
                    LevelNumber = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Price = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    Lots = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    InitialStopRisk = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    RequiredMargin = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    Allowed = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridBacktestPlannedLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GridBacktestPlannedLevels_GridBacktestCycles_GridBacktestCyc~",
                        column: x => x.GridBacktestCycleId,
                        principalTable: "GridBacktestCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GridBacktestLegs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GridBacktestBasketId = table.Column<int>(type: "int", nullable: false),
                    LevelNumber = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FillTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    PlannedPrice = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    FillPrice = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    Lots = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    RequiredMargin = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    InitialStopRisk = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    EntryCommission = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    ExitCommission = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    TotalCommission = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    GrossPnl = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    NetPnl = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridBacktestLegs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GridBacktestLegs_GridBacktestBaskets_GridBacktestBasketId",
                        column: x => x.GridBacktestBasketId,
                        principalTable: "GridBacktestBaskets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GridBacktestBaskets_GridBacktestCycleId",
                table: "GridBacktestBaskets",
                column: "GridBacktestCycleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GridBacktestCycles_GridBacktestRunId_Sequence",
                table: "GridBacktestCycles",
                columns: new[] { "GridBacktestRunId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GridBacktestDiagnostics_GridBacktestRunId_Sequence",
                table: "GridBacktestDiagnostics",
                columns: new[] { "GridBacktestRunId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GridBacktestEvents_GridBacktestRunId_Sequence",
                table: "GridBacktestEvents",
                columns: new[] { "GridBacktestRunId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GridBacktestLegs_GridBacktestBasketId_LevelNumber",
                table: "GridBacktestLegs",
                columns: new[] { "GridBacktestBasketId", "LevelNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GridBacktestPlannedLevels_GridBacktestCycleId_Direction_Leve~",
                table: "GridBacktestPlannedLevels",
                columns: new[] { "GridBacktestCycleId", "Direction", "LevelNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GridBacktestRuns_CreatedAtUtc",
                table: "GridBacktestRuns",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GridBacktestDiagnostics");

            migrationBuilder.DropTable(
                name: "GridBacktestEvents");

            migrationBuilder.DropTable(
                name: "GridBacktestLegs");

            migrationBuilder.DropTable(
                name: "GridBacktestPlannedLevels");

            migrationBuilder.DropTable(
                name: "GridBacktestBaskets");

            migrationBuilder.DropTable(
                name: "GridBacktestCycles");

            migrationBuilder.DropTable(
                name: "GridBacktestRuns");
        }
    }
}
