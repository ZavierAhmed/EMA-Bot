using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmaBot.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperSizingAndReentryControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "MaxReentryAgeBars", table: "TradingSettings", type: "int", nullable: false, defaultValue: 6);
            migrationBuilder.AddColumn<bool>(name: "SameTrendReentryEnabled", table: "TradingSettings", type: "tinyint(1)", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<int>(name: "ReentryAgeBars", table: "PaperTrades", type: "int", nullable: true);
            migrationBuilder.AddColumn<int>(name: "MaxReentryAgeBars", table: "PaperSessions", type: "int", nullable: false, defaultValue: 6);
            migrationBuilder.AddColumn<int>(name: "RejectedByInvalidVolume", table: "PaperSessions", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<bool>(name: "SameTrendReentryEnabled", table: "PaperSessions", type: "tinyint(1)", nullable: false, defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MaxReentryAgeBars", table: "TradingSettings");
            migrationBuilder.DropColumn(name: "SameTrendReentryEnabled", table: "TradingSettings");
            migrationBuilder.DropColumn(name: "ReentryAgeBars", table: "PaperTrades");
            migrationBuilder.DropColumn(name: "MaxReentryAgeBars", table: "PaperSessions");
            migrationBuilder.DropColumn(name: "RejectedByInvalidVolume", table: "PaperSessions");
            migrationBuilder.DropColumn(name: "SameTrendReentryEnabled", table: "PaperSessions");
        }
    }
}
