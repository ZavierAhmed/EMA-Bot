using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class ActivateMt5MarketDataSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MonitoredSymbols_Symbol",
                table: "MonitoredSymbols");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "StrategyOptimizationTrades",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldMaxLength: 32)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "MarketDataSource",
                table: "StrategyOptimizationRuns",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "LegacyBinance")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "StrategyOptimizationMarketResults",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldMaxLength: 32)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "MarketDataSource",
                table: "PaperSessions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "LegacyBinance")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "MonitoredSymbols",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                collation: "utf8mb4_bin",
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldMaxLength: 32)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "QuoteAsset",
                table: "MonitoredSymbols",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(16)",
                oldMaxLength: 16)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "BaseAsset",
                table: "MonitoredSymbols",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldMaxLength: 32)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "MonitoredSymbols",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "MonitoredSymbols",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "LegacyBinance")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "BacktestRuns",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldMaxLength: 32)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "MarketDataSource",
                table: "BacktestRuns",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredSymbols_Source_Symbol",
                table: "MonitoredSymbols",
                columns: new[] { "Source", "Symbol" },
                unique: true);

            migrationBuilder.Sql("UPDATE `MonitoredSymbols` SET `Source` = 'LegacyBinance', `IsEnabled` = 0;");
            migrationBuilder.Sql("UPDATE `BacktestRuns` SET `MarketDataSource` = 'LegacyBinance';");
            migrationBuilder.Sql("UPDATE `PaperSessions` SET `MarketDataSource` = 'LegacyBinance';");
            migrationBuilder.Sql("UPDATE `StrategyOptimizationRuns` SET `MarketDataSource` = 'LegacyBinance';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MonitoredSymbols_Source_Symbol",
                table: "MonitoredSymbols");

            migrationBuilder.DropColumn(
                name: "MarketDataSource",
                table: "StrategyOptimizationRuns");

            migrationBuilder.DropColumn(
                name: "MarketDataSource",
                table: "PaperSessions");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "MonitoredSymbols");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "MonitoredSymbols");

            migrationBuilder.DropColumn(
                name: "MarketDataSource",
                table: "BacktestRuns");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "StrategyOptimizationTrades",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "StrategyOptimizationMarketResults",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "MonitoredSymbols",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64,
                oldCollation: "utf8mb4_bin")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "MonitoredSymbols",
                keyColumn: "QuoteAsset",
                keyValue: null,
                column: "QuoteAsset",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "QuoteAsset",
                table: "MonitoredSymbols",
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(16)",
                oldMaxLength: 16,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.UpdateData(
                table: "MonitoredSymbols",
                keyColumn: "BaseAsset",
                keyValue: null,
                column: "BaseAsset",
                value: "");

            migrationBuilder.AlterColumn<string>(
                name: "BaseAsset",
                table: "MonitoredSymbols",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldMaxLength: 32,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "Symbol",
                table: "BacktestRuns",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoredSymbols_Symbol",
                table: "MonitoredSymbols",
                column: "Symbol",
                unique: true);
        }
    }
}
