using EmaBot.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations;

[DbContext(typeof(EmaBotDbContext))]
[Migration("20260817133000_AddDemoExecutionReconciliationEvidence")]
public partial class AddDemoExecutionReconciliationEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "OrderTicket", table: "DemoExecutions", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>(name: "PositionIdentifier", table: "DemoExecutions", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>(name: "EntryDealTicket", table: "DemoExecutions", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<long>(name: "ExitDealTicket", table: "DemoExecutions", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "ClosedVolumeLots", table: "DemoExecutions", type: "decimal(18,8)", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "AverageClosePrice", table: "DemoExecutions", type: "decimal(18,8)", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "BrokerExecutedAtUtc", table: "DemoExecutions", type: "datetime(6)", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "BrokerClosedAtUtc", table: "DemoExecutions", type: "datetime(6)", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ReconciliationSource", table: "DemoExecutions", type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.CreateIndex(name: "IX_DemoExecutions_PositionIdentifier", table: "DemoExecutions", column: "PositionIdentifier");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_DemoExecutions_PositionIdentifier", table: "DemoExecutions");
        migrationBuilder.DropColumn(name: "OrderTicket", table: "DemoExecutions"); migrationBuilder.DropColumn(name: "PositionIdentifier", table: "DemoExecutions"); migrationBuilder.DropColumn(name: "EntryDealTicket", table: "DemoExecutions"); migrationBuilder.DropColumn(name: "ExitDealTicket", table: "DemoExecutions"); migrationBuilder.DropColumn(name: "ClosedVolumeLots", table: "DemoExecutions"); migrationBuilder.DropColumn(name: "AverageClosePrice", table: "DemoExecutions"); migrationBuilder.DropColumn(name: "BrokerExecutedAtUtc", table: "DemoExecutions"); migrationBuilder.DropColumn(name: "BrokerClosedAtUtc", table: "DemoExecutions"); migrationBuilder.DropColumn(name: "ReconciliationSource", table: "DemoExecutions");
    }
}
