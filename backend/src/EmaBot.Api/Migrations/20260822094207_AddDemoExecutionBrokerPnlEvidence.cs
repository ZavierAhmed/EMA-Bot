using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDemoExecutionBrokerPnlEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BrokerAccountCurrency",
                table: "DemoExecutions",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BrokerCurrentPnlObservedAtUtc",
                table: "DemoExecutions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BrokerCurrentProfit",
                table: "DemoExecutions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BrokerCurrentSwap",
                table: "DemoExecutions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BrokerEntryCommission",
                table: "DemoExecutions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BrokerEntryFee",
                table: "DemoExecutions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BrokerEntryPnlObservedAtUtc",
                table: "DemoExecutions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BrokerEntryProfit",
                table: "DemoExecutions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BrokerEntrySwap",
                table: "DemoExecutions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BrokerHistoryCommission",
                table: "DemoExecutions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BrokerHistoryFee",
                table: "DemoExecutions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BrokerHistoryPnlObservedAtUtc",
                table: "DemoExecutions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BrokerHistoryProfit",
                table: "DemoExecutions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BrokerHistorySwap",
                table: "DemoExecutions",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BrokerAccountCurrency",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerCurrentPnlObservedAtUtc",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerCurrentProfit",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerCurrentSwap",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerEntryCommission",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerEntryFee",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerEntryPnlObservedAtUtc",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerEntryProfit",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerEntrySwap",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerHistoryCommission",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerHistoryFee",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerHistoryPnlObservedAtUtc",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerHistoryProfit",
                table: "DemoExecutions");

            migrationBuilder.DropColumn(
                name: "BrokerHistorySwap",
                table: "DemoExecutions");
        }
    }
}
