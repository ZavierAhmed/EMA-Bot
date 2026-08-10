using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations;

public partial class AddBacktestTradeEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BacktestTradeEvents",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                BacktestTradeId = table.Column<int>(type: "int", nullable: false),
                TimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                EffectiveTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                Type = table.Column<int>(type: "int", nullable: false),
                MarketPrice = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                OldStop = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                NewStop = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                OldTakeProfit = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                NewTakeProfit = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                ProgressPercent = table.Column<decimal>(type: "decimal(65,30)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BacktestTradeEvents", x => x.Id);
                table.ForeignKey("FK_BacktestTradeEvents_BacktestTrades_BacktestTradeId", x => x.BacktestTradeId, "BacktestTrades", "Id", onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateIndex(name: "IX_BacktestTradeEvents_BacktestTradeId", table: "BacktestTradeEvents", column: "BacktestTradeId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "BacktestTradeEvents");
}
