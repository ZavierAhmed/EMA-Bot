using System;
using EmaBot.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations;

[DbContext(typeof(EmaBotDbContext))]
[Migration("20260822010000_AddDemoStrategyPositionManagement")]
public partial class AddDemoStrategyPositionManagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "TrailingStopEnabled", table: "DemoStrategySessions", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "ExitOnOppositeCrossover", table: "DemoStrategySessions", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.CreateTable(
            name: "DemoStrategyPositionManagement",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                DemoStrategySessionId = table.Column<int>(type: "int", nullable: false),
                DemoStrategySessionSymbolId = table.Column<int>(type: "int", nullable: false),
                DemoStrategyIntentId = table.Column<int>(type: "int", nullable: false),
                DemoExecutionId = table.Column<int>(type: "int", nullable: false),
                State = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                OriginalEntryPrice = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                OriginalStopLoss = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                OriginalTakeProfit = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                BestFavorablePrice = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                BestFavorableProgressPercent = table.Column<decimal>(type: "decimal(18,8)", nullable: false),
                TakeProfitExtensionState = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                TargetExtensionAppliedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                HighestAttemptedLockPercent = table.Column<decimal>(type: "decimal(8,4)", nullable: false),
                HighestAppliedLockPercent = table.Column<decimal>(type: "decimal(8,4)", nullable: false),
                PendingProtectionActionId = table.Column<Guid>(type: "char(36)", nullable: true),
                PendingProtectionLockPercent = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                PendingProtectionExtendsTarget = table.Column<bool>(type: "tinyint(1)", nullable: false),
                PendingDesiredStopLoss = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                PendingDesiredTakeProfit = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                OppositeSignalTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                OppositeSignalDirection = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: true),
                OppositeCloseState = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                OppositeCloseRequestedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                LastManagedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                LastReason = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DemoStrategyPositionManagement", x => x.Id);
                table.ForeignKey("FK_DemoStrategyPositionManagement_DemoExecutions_DemoExecutionId", x => x.DemoExecutionId, "DemoExecutions", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_DemoStrategyPositionManagement_DemoStrategyIntents_DemoStrategyIntentId", x => x.DemoStrategyIntentId, "DemoStrategyIntents", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_DemoStrategyPositionManagement_DemoStrategySessionSymbols_DemoStrategySessionSymbolId", x => x.DemoStrategySessionSymbolId, "DemoStrategySessionSymbols", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_DemoStrategyPositionManagement_DemoStrategySessions_DemoStrategySessionId", x => x.DemoStrategySessionId, "DemoStrategySessions", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(name: "IX_DemoStrategyPositionManagement_DemoExecutionId", table: "DemoStrategyPositionManagement", column: "DemoExecutionId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_DemoStrategyPositionManagement_DemoStrategyIntentId", table: "DemoStrategyPositionManagement", column: "DemoStrategyIntentId");
        migrationBuilder.CreateIndex(name: "IX_DemoStrategyPositionManagement_DemoStrategySessionId_State", table: "DemoStrategyPositionManagement", columns: new[] { "DemoStrategySessionId", "State" });
        migrationBuilder.CreateIndex(name: "IX_DemoStrategyPositionManagement_DemoStrategySessionSymbolId", table: "DemoStrategyPositionManagement", column: "DemoStrategySessionSymbolId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DemoStrategyPositionManagement");
        migrationBuilder.DropColumn(name: "TrailingStopEnabled", table: "DemoStrategySessions");
        migrationBuilder.DropColumn(name: "ExitOnOppositeCrossover", table: "DemoStrategySessions");
    }
}
