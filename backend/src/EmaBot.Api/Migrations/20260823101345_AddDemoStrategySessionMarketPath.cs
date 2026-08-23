using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoStrategySessionMarketPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DemoStrategySessionCandles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DemoStrategySessionSymbolId = table.Column<int>(type: "int", nullable: false),
                    OpenTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    CloseTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Volume = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Ema9 = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    Ema15 = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    Ema100 = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    ObservationOrigin = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DemoStrategySessionCandles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DemoStrategySessionCandles_DemoStrategySessionSymbols_DemoSt~",
                        column: x => x.DemoStrategySessionSymbolId,
                        principalTable: "DemoStrategySessionSymbols",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DemoStrategySessionCandles_DemoStrategySessionSymbolId_Close~",
                table: "DemoStrategySessionCandles",
                columns: new[] { "DemoStrategySessionSymbolId", "CloseTimeUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DemoStrategySessionCandles");
        }
    }
}
