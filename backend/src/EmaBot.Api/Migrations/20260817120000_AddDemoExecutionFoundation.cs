using EmaBot.Api.Data;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations;

[DbContext(typeof(EmaBotDbContext))]
[Migration("20260817120000_AddDemoExecutionFoundation")]
public partial class AddDemoExecutionFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DemoExecutions",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                ClientExecutionId = table.Column<Guid>(type: "char(36)", nullable: false),
                State = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                Provider = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                ExpectedAccountFingerprint = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                ExpectedServer = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                BrokerSymbol = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false).Annotation("Relational:Collation", "utf8mb4_bin"),
                Side = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false),
                VolumeLots = table.Column<decimal>(type: "decimal(18,8)", nullable: false), RequestedStopLoss = table.Column<decimal>(type: "decimal(18,8)", nullable: true), RequestedTakeProfit = table.Column<decimal>(type: "decimal(18,8)", nullable: true), MagicNumber = table.Column<long>(type: "bigint", nullable: false), CorrelationMarker = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false), PositionTicket = table.Column<long>(type: "bigint", nullable: true), DealTicket = table.Column<long>(type: "bigint", nullable: true), FilledVolumeLots = table.Column<decimal>(type: "decimal(18,8)", nullable: true), AverageFillPrice = table.Column<decimal>(type: "decimal(18,8)", nullable: true), BrokerRetcode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true), BrokerMessage = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true), CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false), PreflightAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true), SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true), BrokerAcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true), ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true), ReconciledAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true), ReconciliationNote = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true)
            }, constraints: table => table.PrimaryKey("PK_DemoExecutions", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_DemoExecutions_ClientExecutionId", table: "DemoExecutions", column: "ClientExecutionId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_DemoExecutions_PositionTicket", table: "DemoExecutions", column: "PositionTicket");
        migrationBuilder.CreateIndex(name: "IX_DemoExecutions_State", table: "DemoExecutions", column: "State");
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "DemoExecutions");
}
