using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNativeBacktestSizingProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NativePositionSizingMode",
                table: "BacktestTrades",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NativeFixedLots",
                table: "BacktestRuns",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NativeMarginPerTradePercent",
                table: "BacktestRuns",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NativePositionSizingMode",
                table: "BacktestRuns",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NativePositionSizingMode",
                table: "BacktestTrades");

            migrationBuilder.DropColumn(
                name: "NativeFixedLots",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "NativeMarginPerTradePercent",
                table: "BacktestRuns");

            migrationBuilder.DropColumn(
                name: "NativePositionSizingMode",
                table: "BacktestRuns");
        }
    }
}
