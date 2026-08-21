using System;
using EmaBot.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations;

[DbContext(typeof(EmaBotDbContext))]
[Migration("20260821113000_AddDemoExecutionManagementActions")]
public partial class AddDemoExecutionManagementActions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "CurrentStopLoss", table: "DemoExecutions", type: "decimal(18,8)", nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "CurrentTakeProfit", table: "DemoExecutions", type: "decimal(18,8)", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "ProtectionObservedAtUtc", table: "DemoExecutions", type: "datetime(6)", nullable: true);
        migrationBuilder.CreateTable(
            name: "DemoExecutionManagementActions",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                ClientManagementActionId = table.Column<Guid>(type: "char(36)", nullable: false),
                DemoExecutionId = table.Column<int>(type: "int", nullable: false),
                Kind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                State = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                RequestedStopLoss = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                RequestedTakeProfit = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                ObservedBeforeStopLoss = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                ObservedBeforeTakeProfit = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                AppliedStopLoss = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                AppliedTakeProfit = table.Column<decimal>(type: "decimal(18,8)", nullable: true),
                BrokerRetcode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                BrokerMessage = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                ReconciledAtUtc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                ReconciliationNote = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                ReconciliationSource = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DemoExecutionManagementActions", x => x.Id);
                table.ForeignKey("FK_DemoExecutionManagementActions_DemoExecutions_DemoExecutionId", x => x.DemoExecutionId, "DemoExecutions", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(name: "IX_DemoExecutionManagementActions_ClientManagementActionId", table: "DemoExecutionManagementActions", column: "ClientManagementActionId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_DemoExecutionManagementActions_DemoExecutionId_State", table: "DemoExecutionManagementActions", columns: new[] { "DemoExecutionId", "State" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "DemoExecutionManagementActions");
        migrationBuilder.DropColumn(name: "CurrentStopLoss", table: "DemoExecutions");
        migrationBuilder.DropColumn(name: "CurrentTakeProfit", table: "DemoExecutions");
        migrationBuilder.DropColumn(name: "ProtectionObservedAtUtc", table: "DemoExecutions");
    }
}
