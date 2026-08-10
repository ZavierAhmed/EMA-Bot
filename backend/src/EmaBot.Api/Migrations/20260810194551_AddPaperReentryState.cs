using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations;

public partial class AddPaperReentryState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "PendingIsReentry", table: "PaperSessionSymbols", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "PendingTrendRegimeCrossoverTimeUtc", table: "PaperSessionSymbols", type: "datetime(6)", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "ReentryConsumed", table: "PaperSessionSymbols", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "ReentryEligible", table: "PaperSessionSymbols", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "TrendRegimeCrossoverTimeUtc", table: "PaperSessionSymbols", type: "datetime(6)", nullable: true);
        migrationBuilder.AddColumn<int>(name: "TrendRegimeDirection", table: "PaperSessionSymbols", type: "int", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PendingIsReentry", table: "PaperSessionSymbols");
        migrationBuilder.DropColumn(name: "PendingTrendRegimeCrossoverTimeUtc", table: "PaperSessionSymbols");
        migrationBuilder.DropColumn(name: "ReentryConsumed", table: "PaperSessionSymbols");
        migrationBuilder.DropColumn(name: "ReentryEligible", table: "PaperSessionSymbols");
        migrationBuilder.DropColumn(name: "TrendRegimeCrossoverTimeUtc", table: "PaperSessionSymbols");
        migrationBuilder.DropColumn(name: "TrendRegimeDirection", table: "PaperSessionSymbols");
    }
}
