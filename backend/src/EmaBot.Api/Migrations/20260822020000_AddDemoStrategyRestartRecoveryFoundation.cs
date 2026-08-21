using EmaBot.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations;

[DbContext(typeof(EmaBotDbContext))]
[Migration("20260822020000_AddDemoStrategyRestartRecoveryFoundation")]
public partial class AddDemoStrategyRestartRecoveryFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "NativeExitReason", table: "DemoExecutions", type: "varchar(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<bool>(name: "NativeExitReasonConflicted", table: "DemoExecutions", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "SameTrendReentryEnabled", table: "DemoStrategySessions", type: "tinyint(1)", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>(name: "MaxReentryAgeBars", table: "DemoStrategySessions", type: "int", nullable: false, defaultValue: 6);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "NativeExitReason", table: "DemoExecutions");
        migrationBuilder.DropColumn(name: "NativeExitReasonConflicted", table: "DemoExecutions");
        migrationBuilder.DropColumn(name: "SameTrendReentryEnabled", table: "DemoStrategySessions");
        migrationBuilder.DropColumn(name: "MaxReentryAgeBars", table: "DemoStrategySessions");
    }
}
