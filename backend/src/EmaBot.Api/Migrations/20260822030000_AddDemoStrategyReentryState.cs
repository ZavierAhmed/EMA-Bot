using EmaBot.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations;

[DbContext(typeof(EmaBotDbContext))]
[Migration("20260822030000_AddDemoStrategyReentryState")]
public partial class AddDemoStrategyReentryState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "IsReentry", table: "DemoStrategyIntents", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int?>(name: "ReentryAgeBars", table: "DemoStrategyIntents", type: "int", nullable: true);
        migrationBuilder.AddColumn<int?>(name: "ReentrySourceDemoExecutionId", table: "DemoStrategyIntents", type: "int", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset?>(name: "TrendRegimeCrossoverTimeUtc", table: "DemoStrategyIntents", type: "datetime(6)", nullable: true);

        migrationBuilder.AddColumn<bool>(name: "ReentryConsumed", table: "DemoStrategySessionSymbols", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "ReentryEligible", table: "DemoStrategySessionSymbols", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTimeOffset?>(name: "ReentryEligibleAtUtc", table: "DemoStrategySessionSymbols", type: "datetime(6)", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ReentryReason", table: "DemoStrategySessionSymbols", type: "varchar(1024)", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<int?>(name: "ReentrySourceDemoExecutionId", table: "DemoStrategySessionSymbols", type: "int", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset?>(name: "TrendRegimeCrossoverTimeUtc", table: "DemoStrategySessionSymbols", type: "datetime(6)", nullable: true);
        migrationBuilder.AddColumn<string>(name: "TrendRegimeDirection", table: "DemoStrategySessionSymbols", type: "varchar(8)", maxLength: 8, nullable: true);

        migrationBuilder.CreateIndex(name: "IX_DemoStrategyIntents_ReentrySourceDemoExecutionId", table: "DemoStrategyIntents", column: "ReentrySourceDemoExecutionId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_DemoStrategySessionSymbols_ReentrySourceDemoExecutionId", table: "DemoStrategySessionSymbols", column: "ReentrySourceDemoExecutionId");
        migrationBuilder.AddForeignKey(name: "FK_DemoStrategyIntents_DemoExecutions_ReentrySourceDemoExecutionId", table: "DemoStrategyIntents", column: "ReentrySourceDemoExecutionId", principalTable: "DemoExecutions", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_DemoStrategySessionSymbols_DemoExecutions_ReentrySourceDemoExecutionId", table: "DemoStrategySessionSymbols", column: "ReentrySourceDemoExecutionId", principalTable: "DemoExecutions", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_DemoStrategyIntents_DemoExecutions_ReentrySourceDemoExecutionId", table: "DemoStrategyIntents");
        migrationBuilder.DropForeignKey(name: "FK_DemoStrategySessionSymbols_DemoExecutions_ReentrySourceDemoExecutionId", table: "DemoStrategySessionSymbols");
        migrationBuilder.DropIndex(name: "IX_DemoStrategyIntents_ReentrySourceDemoExecutionId", table: "DemoStrategyIntents");
        migrationBuilder.DropIndex(name: "IX_DemoStrategySessionSymbols_ReentrySourceDemoExecutionId", table: "DemoStrategySessionSymbols");

        migrationBuilder.DropColumn(name: "IsReentry", table: "DemoStrategyIntents");
        migrationBuilder.DropColumn(name: "ReentryAgeBars", table: "DemoStrategyIntents");
        migrationBuilder.DropColumn(name: "ReentrySourceDemoExecutionId", table: "DemoStrategyIntents");
        migrationBuilder.DropColumn(name: "TrendRegimeCrossoverTimeUtc", table: "DemoStrategyIntents");
        migrationBuilder.DropColumn(name: "ReentryConsumed", table: "DemoStrategySessionSymbols");
        migrationBuilder.DropColumn(name: "ReentryEligible", table: "DemoStrategySessionSymbols");
        migrationBuilder.DropColumn(name: "ReentryEligibleAtUtc", table: "DemoStrategySessionSymbols");
        migrationBuilder.DropColumn(name: "ReentryReason", table: "DemoStrategySessionSymbols");
        migrationBuilder.DropColumn(name: "ReentrySourceDemoExecutionId", table: "DemoStrategySessionSymbols");
        migrationBuilder.DropColumn(name: "TrendRegimeCrossoverTimeUtc", table: "DemoStrategySessionSymbols");
        migrationBuilder.DropColumn(name: "TrendRegimeDirection", table: "DemoStrategySessionSymbols");
    }
}
