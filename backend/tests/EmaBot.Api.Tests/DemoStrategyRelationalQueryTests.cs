using EmaBot.Api.Data;
using EmaBot.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Tests;

public sealed class DemoStrategyRelationalQueryTests
{
    [Fact]
    public void BrokerSymbolScopedUnresolvedExecutionQuery_TranslatesWithConfiguredMySqlRelationalProvider()
    {
        var options = new DbContextOptionsBuilder<EmaBotDbContext>()
            .UseMySql("Server=localhost;Database=emabot_query_translation;", new MySqlServerVersion(new Version(8, 4, 0)))
            .Options;
        using var database = new EmaBotDbContext(options);
        var sql = DemoStrategyCoordinator.UnresolvedExecutionsForBrokerSymbol(database, "XAUUSDm").ToQueryString();
        Assert.Contains("DemoExecutions", sql);
        Assert.Contains("BrokerSymbol", sql);
        Assert.Contains("PreflightPassed", sql);
        Assert.Contains("Submitting", sql);
        Assert.Contains("BrokerAccepted", sql);
        Assert.Contains("PartiallyFilled", sql);
        Assert.Contains("Open", sql);
        Assert.Contains("CloseRequested", sql);
        Assert.Contains("ReconciliationRequired", sql);
        Assert.DoesNotContain("DemoStrategyIntents", sql);
    }
}
