using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace EmaBot.Api.Migrations;

[Migration("20260813173000_PersistPaperDecisionLedger")]
public partial class PersistPaperDecisionLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PaperDecisionEvents",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                PaperSessionId = table.Column<int>(type: "int", nullable: false),
                PaperSessionSymbolId = table.Column<int>(type: "int", nullable: false),
                TimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                CandleCloseTimeUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                Stage = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                Direction = table.Column<int>(type: "int", nullable: true),
                Message = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                Ema9 = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true), Ema15 = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true), Ema100 = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true), GapPercent = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true), GapState = table.Column<int>(type: "int", nullable: true), StopPrice = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true), StopSource = table.Column<int>(type: "int", nullable: true), ExpectedEntryOpenUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true), Bid = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true), Ask = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true), EntryPrice = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true), Lots = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true), RequiredMargin = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaperDecisionEvents", x => x.Id);
                table.ForeignKey("FK_PaperDecisionEvents_PaperSessions_PaperSessionId", x => x.PaperSessionId, "PaperSessions", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_PaperDecisionEvents_PaperSessionSymbols_PaperSessionSymbolId", x => x.PaperSessionSymbolId, "PaperSessionSymbols", "Id", onDelete: ReferentialAction.Cascade);
            })
            .Annotation("MySql:CharSet", "utf8mb4");
        migrationBuilder.CreateIndex(name: "IX_PaperDecisionEvents_PaperSessionId_TimeUtc", table: "PaperDecisionEvents", columns: new[] { "PaperSessionId", "TimeUtc" });
        migrationBuilder.CreateIndex(name: "IX_PaperDecisionEvents_PaperSessionSymbolId_TimeUtc", table: "PaperDecisionEvents", columns: new[] { "PaperSessionSymbolId", "TimeUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "PaperDecisionEvents");
}
