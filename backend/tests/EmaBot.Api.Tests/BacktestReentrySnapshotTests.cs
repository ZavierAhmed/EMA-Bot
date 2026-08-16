using EmaBot.Api.Controllers;
using EmaBot.Api.Data;
using EmaBot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Tests;

public sealed class BacktestReentrySnapshotTests
{
    [Fact]
    public async Task SavedBacktestResponse_UsesItsFrozenReentrySettingsAfterGlobalSettingsChange()
    {
        var options = new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new EmaBotDbContext(options);
        var global = new TradingSettings { Id = 1, SameTrendReentryEnabled = true, MaxReentryAgeBars = 5, ExitOnOppositeCrossover = true };
        var run = new BacktestRun { Symbol = "BTCUSDm", Interval = "3m", SameTrendReentryEnabled = global.SameTrendReentryEnabled, MaxReentryAgeBars = global.MaxReentryAgeBars, ExitOnOppositeCrossover = global.ExitOnOppositeCrossover };
        database.AddRange(global, run); await database.SaveChangesAsync();

        global.SameTrendReentryEnabled = false; global.MaxReentryAgeBars = 9; global.ExitOnOppositeCrossover = false; await database.SaveChangesAsync();
        var saved = await database.BacktestRuns.AsNoTracking().SingleAsync();
        var response = BacktestResponseMapper.ToDetail(saved);

        Assert.True(response.SameTrendReentryEnabled); Assert.Equal(5, response.MaxReentryAgeBars); Assert.True(response.ExitOnOppositeCrossover);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    [InlineData(101, false)]
    public void ReentryAgeBounds_AreInclusiveFromOneThroughOneHundred(int value, bool valid)
    {
        Assert.Equal(valid, value is >= 1 and <= 100);
    }
}
