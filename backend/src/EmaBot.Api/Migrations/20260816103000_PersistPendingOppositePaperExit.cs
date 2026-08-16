using EmaBot.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations;

[DbContext(typeof(EmaBotDbContext))]
[Migration("20260816103000_PersistPendingOppositePaperExit")]
public partial class PersistPendingOppositePaperExit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "PendingOppositeExitDirection", table: "PaperSessionSymbols", type: "int", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "PendingOppositeExitSignalTimeUtc", table: "PaperSessionSymbols", type: "datetime(6)", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PendingOppositeExitDirection", table: "PaperSessionSymbols");
        migrationBuilder.DropColumn(name: "PendingOppositeExitSignalTimeUtc", table: "PaperSessionSymbols");
    }
}
