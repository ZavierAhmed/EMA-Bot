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
        Assert.True(Array.IndexOf(migrations, "20260813115102_ActivateBrokerAwareMt5PaperTrading") < Array.IndexOf(migrations, "20260813173000_PersistPaperDecisionLedger"));
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
