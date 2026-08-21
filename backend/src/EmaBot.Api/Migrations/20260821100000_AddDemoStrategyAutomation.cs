using System;
using EmaBot.Api.Data;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations;

[DbContext(typeof(EmaBotDbContext))]
[Migration("20260821100000_AddDemoStrategyAutomation")]
public partial class AddDemoStrategyAutomation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DemoStrategySessions",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                Interval = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                StoppedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                InterruptedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                FailureMessage = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                AutomationEnabledAtCreation = table.Column<bool>(type: "tinyint(1)", nullable: false),
                FixedLots = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                RiskReward = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                MinEmaGapPercent = table.Column<decimal>(type: "decimal(8,4)", nullable: false),
                MaxStopDistancePercent = table.Column<decimal>(type: "decimal(8,4)", nullable: false),
                WaitForConfirmationCandle = table.Column<bool>(type: "tinyint(1)", nullable: false),
                UseEma100Filter = table.Column<bool>(type: "tinyint(1)", nullable: false),
                UseAdaptiveInitialStop = table.Column<bool>(type: "tinyint(1)", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DemoStrategySessions", x => x.Id));
        migrationBuilder.CreateTable(
            name: "DemoStrategySessionSymbols",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                DemoStrategySessionId = table.Column<int>(type: "int", nullable: false),
                Symbol = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                BrokerSymbol = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false).Annotation("Relational:Collation", "utf8mb4_bin"),
                LastProcessedClosedCandleUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                LastMarketEventUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DemoStrategySessionSymbols", x => x.Id);
                table.ForeignKey("FK_DemoStrategySessionSymbols_DemoStrategySessions_DemoStrategySessionId", x => x.DemoStrategySessionId, "DemoStrategySessions", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateTable(
            name: "DemoStrategyIntents",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                DemoStrategySessionId = table.Column<int>(type: "int", nullable: false),
                DemoStrategySessionSymbolId = table.Column<int>(type: "int", nullable: false),
                Direction = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false),
                CrossoverTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                SignalTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                ExpectedEntryOpenUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                SignalOpen = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                SignalClose = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                SignalEma9 = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                SignalEma15 = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                SignalEma100 = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                SignalGapPercent = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                SignalGapState = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                StructuralStopLoss = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                StopSourceType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                StopSourceTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                IntendedTakeProfit = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                IntendedVolumeLots = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                ClientExecutionId = table.Column<Guid>(type: "char(36)", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                DemoExecutionId = table.Column<int>(type: "int", nullable: true),
                Reason = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DemoStrategyIntents", x => x.Id);
                table.ForeignKey("FK_DemoStrategyIntents_DemoExecutions_DemoExecutionId", x => x.DemoExecutionId, "DemoExecutions", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_DemoStrategyIntents_DemoStrategySessionSymbols_DemoStrategySessionSymbolId", x => x.DemoStrategySessionSymbolId, "DemoStrategySessionSymbols", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_DemoStrategyIntents_DemoStrategySessions_DemoStrategySessionId", x => x.DemoStrategySessionId, "DemoStrategySessions", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(name: "IX_DemoStrategySessions_Status", table: "DemoStrategySessions", column: "Status");
        migrationBuilder.CreateIndex(name: "IX_DemoStrategySessionSymbols_DemoStrategySessionId_Symbol", table: "DemoStrategySessionSymbols", columns: new[] { "DemoStrategySessionId", "Symbol" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_DemoStrategyIntents_ClientExecutionId", table: "DemoStrategyIntents", column: "ClientExecutionId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_DemoStrategyIntents_DemoExecutionId", table: "DemoStrategyIntents", column: "DemoExecutionId");
        migrationBuilder.CreateIndex(name: "IX_DemoStrategyIntents_DemoStrategySessionId_DemoStrategySessionSymbolId_SignalTimeUtc_Direction", table: "DemoStrategyIntents", columns: new[] { "DemoStrategySessionId", "DemoStrategySessionSymbolId", "SignalTimeUtc", "Direction" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_DemoStrategyIntents_DemoStrategySessionSymbolId_Status", table: "DemoStrategyIntents", columns: new[] { "DemoStrategySessionSymbolId", "Status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DemoStrategyIntents");
        migrationBuilder.DropTable(name: "DemoStrategySessionSymbols");
        migrationBuilder.DropTable(name: "DemoStrategySessions");
    }
}
