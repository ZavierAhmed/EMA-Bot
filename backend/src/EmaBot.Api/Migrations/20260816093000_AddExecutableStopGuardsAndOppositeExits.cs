using EmaBot.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations;

[DbContext(typeof(EmaBotDbContext))]
[Migration("20260816093000_AddExecutableStopGuardsAndOppositeExits")]
public partial class AddExecutableStopGuardsAndOppositeExits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "ExitOnOppositeCrossover", table: "TradingSettings", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "ExitOnOppositeCrossover", table: "PaperSessions", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>(name: "RejectedByExecutableStop", table: "PaperSessions", type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<bool>(name: "ExitOnOppositeCrossover", table: "BacktestRuns", type: "tinyint(1)", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ExitOnOppositeCrossover", table: "TradingSettings");
        migrationBuilder.DropColumn(name: "ExitOnOppositeCrossover", table: "PaperSessions");
        migrationBuilder.DropColumn(name: "RejectedByExecutableStop", table: "PaperSessions");
        migrationBuilder.DropColumn(name: "ExitOnOppositeCrossover", table: "BacktestRuns");
    }
}
