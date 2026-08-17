using EmaBot.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Tests;

public sealed class PomeloQueryRegressionTests
{
    [Fact]
    public void PaperDecisionLedgerMigration_IsDiscoveredForEmaBotDbContext()
    {
        var options = new DbContextOptionsBuilder<EmaBotDbContext>()
            .UseMySql("Server=localhost;Database=emabot_migration_discovery_test;", new MySqlServerVersion(new Version(8, 4, 0)))
            .Options;

        using var database = new EmaBotDbContext(options);
        var migrations = database.Database.GetMigrations().ToArray();

        Assert.Contains("20260813115102_ActivateBrokerAwareMt5PaperTrading", migrations);
        Assert.Contains("20260813173000_PersistPaperDecisionLedger", migrations);
        Assert.Contains("20260814185858_AddAdaptiveInitialStop", migrations);
        Assert.Contains("20260815153839_AddPaperSizingAndReentryControls", migrations);
        Assert.Contains("20260815160635_AddBacktestReentrySnapshot", migrations);
        Assert.Contains("20260816093000_AddExecutableStopGuardsAndOppositeExits", migrations);
        Assert.Contains("20260816103000_PersistPendingOppositePaperExit", migrations);
        Assert.Contains("20260816113000_AddPaperInitialRiskAmount", migrations);
        Assert.Contains("20260817120000_AddDemoExecutionFoundation", migrations);
        Assert.True(Array.IndexOf(migrations, "20260813115102_ActivateBrokerAwareMt5PaperTrading") < Array.IndexOf(migrations, "20260813173000_PersistPaperDecisionLedger"));
        Assert.True(Array.IndexOf(migrations, "20260813173000_PersistPaperDecisionLedger") < Array.IndexOf(migrations, "20260814185858_AddAdaptiveInitialStop"));
        Assert.True(Array.IndexOf(migrations, "20260814185858_AddAdaptiveInitialStop") < Array.IndexOf(migrations, "20260815153839_AddPaperSizingAndReentryControls"));
        Assert.True(Array.IndexOf(migrations, "20260815153839_AddPaperSizingAndReentryControls") < Array.IndexOf(migrations, "20260815160635_AddBacktestReentrySnapshot"));
        Assert.True(Array.IndexOf(migrations, "20260815160635_AddBacktestReentrySnapshot") < Array.IndexOf(migrations, "20260816093000_AddExecutableStopGuardsAndOppositeExits"));
        Assert.True(Array.IndexOf(migrations, "20260816093000_AddExecutableStopGuardsAndOppositeExits") < Array.IndexOf(migrations, "20260816103000_PersistPendingOppositePaperExit"));
        Assert.True(Array.IndexOf(migrations, "20260816103000_PersistPendingOppositePaperExit") < Array.IndexOf(migrations, "20260816113000_AddPaperInitialRiskAmount"));
        Assert.True(Array.IndexOf(migrations, "20260816113000_AddPaperInitialRiskAmount") < Array.IndexOf(migrations, "20260817120000_AddDemoExecutionFoundation"));
    }

    [Fact]
    public void MonitoredSymbolSelection_CompilesWithPomeloAndParameterizedCollectionsAsConstants()
    {
        var options = new DbContextOptionsBuilder<EmaBotDbContext>()
            .UseMySql(
                "Server=localhost;Database=emabot_query_translation_test;",
                new MySqlServerVersion(new Version(8, 4, 0)),
                mySqlOptions =>
                {
                    mySqlOptions.EnableRetryOnFailure();
                    mySqlOptions.TranslateParameterizedCollectionsToConstants();
                })
            .Options;
        IReadOnlyList<string> requestedSymbols = ["BTCUSDT", "ETHUSDT"];

        using var database = new EmaBotDbContext(options);
        var sql = database.MonitoredSymbols
            .Where(symbol => requestedSymbols.Contains(symbol.Symbol) && symbol.IsEnabled)
            .ToQueryString();

        Assert.Contains("BTCUSDT", sql);
        Assert.Contains("ETHUSDT", sql);
    }
}
