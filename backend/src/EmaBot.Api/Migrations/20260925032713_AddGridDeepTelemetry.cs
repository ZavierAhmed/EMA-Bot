using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGridDeepTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GridBacktestTelemetry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    GridBacktestCycleId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    TimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Direction = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BidOpen = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    BidHigh = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    BidLow = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    BidClose = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    SpreadPoints = table.Column<int>(type: "int", nullable: false),
                    SpreadPrice = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    CurrentAtr = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    CurrentAdx = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    CurrentRangeHigh = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    CurrentRangeLow = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    DistanceFromAnchorSpacings = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    AdverseDistanceFromAnchorSpacings = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    AdxDeltaFromQualification = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    AtrRatioToQualification = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    CandleBody = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    CandleTrueRange = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    CandleBodyAtrRatio = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    CandleTrueRangeAtrRatio = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: true),
                    FrozenBoundaryPrice = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    CloseBeyondFrozenBoundary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AdverseExtremeBeyondFrozenBoundary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    BreakoutDistanceSpacings = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    ConsecutiveAdverseCloses = table.Column<int>(type: "int", nullable: false),
                    ConsecutiveClosesBeyondFrozenBoundary = table.Column<int>(type: "int", nullable: false),
                    MaxFilledLevelBeforeBar = table.Column<int>(type: "int", nullable: false),
                    MaxFilledLevelAfterBar = table.Column<int>(type: "int", nullable: false),
                    NewFillCount = table.Column<int>(type: "int", nullable: false),
                    DeepestConfiguredLevel = table.Column<int>(type: "int", nullable: false),
                    DeepestLevelFilled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ExitReasonThisBar = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridBacktestTelemetry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GridBacktestTelemetry_GridBacktestCycles_GridBacktestCycleId",
                        column: x => x.GridBacktestCycleId,
                        principalTable: "GridBacktestCycles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GridBacktestTelemetry_GridBacktestCycleId_Sequence",
                table: "GridBacktestTelemetry",
                columns: new[] { "GridBacktestCycleId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GridBacktestTelemetry_GridBacktestCycleId_TimeUtc",
                table: "GridBacktestTelemetry",
                columns: new[] { "GridBacktestCycleId", "TimeUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GridBacktestTelemetry");
        }
    }
}
