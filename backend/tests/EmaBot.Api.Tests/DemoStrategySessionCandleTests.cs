using EmaBot.Api.Data;
using EmaBot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EmaBot.Api.Tests;

public sealed class DemoStrategySessionCandleTests
{
    [Fact]
    public void Model_MapsClosedCandleEvidenceWithTheRequiredUniqueIdentity()
    {
        using var database = NewDatabase();
        var entity = database.Model.FindEntityType(typeof(DemoStrategySessionCandle));

        Assert.NotNull(entity);
        Assert.Contains(entity!.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([nameof(DemoStrategySessionCandle.DemoStrategySessionSymbolId), nameof(DemoStrategySessionCandle.CloseTimeUtc)]));
        Assert.Equal("string", entity.FindProperty(nameof(DemoStrategySessionCandle.ObservationOrigin))!.GetProviderClrType()!.Name.ToLowerInvariant());
        Assert.Equal(18, entity.FindProperty(nameof(DemoStrategySessionCandle.Close))!.GetPrecision());
        Assert.Equal(8, entity.FindProperty(nameof(DemoStrategySessionCandle.Close))!.GetScale());
    }

    [Fact]
    public async Task DistinctSessionsAndSymbols_RoundTripSameCloseTimeWithoutCrossingEvidence()
    {
        await using var database = NewDatabase();
        var first = new DemoStrategySession { Interval = "3m", Status = DemoStrategySessionStatus.Stopped, CreatedAtUtc = DateTimeOffset.UtcNow, Symbols = [new DemoStrategySessionSymbol { Symbol = "BTCUSDm", BrokerSymbol = "BTCUSDm" }, new DemoStrategySessionSymbol { Symbol = "XAUUSDm", BrokerSymbol = "XAUUSDm" }] };
        var second = new DemoStrategySession { Interval = "3m", Status = DemoStrategySessionStatus.Stopped, CreatedAtUtc = DateTimeOffset.UtcNow, Symbols = [new DemoStrategySessionSymbol { Symbol = "BTCUSDm", BrokerSymbol = "BTCUSDm" }] };
        database.AddRange(first, second); await database.SaveChangesAsync();
        var close = new DateTimeOffset(2026, 8, 23, 12, 3, 0, TimeSpan.Zero);
        database.DemoStrategySessionCandles.AddRange(Candle(first.Symbols[0].Id, close, DemoStrategySessionCandleObservationOrigin.BootstrapHistory), Candle(first.Symbols[1].Id, close, DemoStrategySessionCandleObservationOrigin.LiveClosedCandle), Candle(second.Symbols[0].Id, close, DemoStrategySessionCandleObservationOrigin.RecoveryReplay));
        await database.SaveChangesAsync();

        var rows = await database.DemoStrategySessionCandles.AsNoTracking().OrderBy(item => item.Id).ToArrayAsync();
        Assert.Equal(3, rows.Length); Assert.Equal(3, rows.Select(item => item.DemoStrategySessionSymbolId).Distinct().Count()); Assert.All(rows, item => Assert.Equal(close, item.CloseTimeUtc));
    }

    [Fact]
    public async Task OhlcvNullableEmasOriginAndObservationTime_RoundTripWithoutPrecisionLoss()
    {
        await using var database = NewDatabase();
        var session = new DemoStrategySession { Interval = "3m", Status = DemoStrategySessionStatus.Stopped, CreatedAtUtc = DateTimeOffset.UtcNow, Symbols = [new DemoStrategySessionSymbol { Symbol = "BTCUSDm", BrokerSymbol = "BTCUSDm" }] };
        database.Add(session); await database.SaveChangesAsync();
        var close = new DateTimeOffset(2026, 8, 23, 12, 3, 0, TimeSpan.Zero); var observed = close.AddSeconds(7);
        database.DemoStrategySessionCandles.Add(new DemoStrategySessionCandle { DemoStrategySessionSymbolId = session.Symbols[0].Id, OpenTimeUtc = close.AddMinutes(-3), CloseTimeUtc = close, Open = 78234.12345678m, High = 78235.12345678m, Low = 78233.12345678m, Close = 78234.98765432m, Volume = 12.34567891m, Ema9 = 78234.11111111m, Ema15 = null, Ema100 = 78200.22222222m, ObservationOrigin = DemoStrategySessionCandleObservationOrigin.RecoveryReplay, ObservedAtUtc = observed });
        await database.SaveChangesAsync();

        var row = await database.DemoStrategySessionCandles.AsNoTracking().SingleAsync();
        Assert.Equal(78234.12345678m, row.Open); Assert.Equal(78235.12345678m, row.High); Assert.Equal(78233.12345678m, row.Low); Assert.Equal(78234.98765432m, row.Close); Assert.Equal(12.34567891m, row.Volume);
        Assert.Equal(78234.11111111m, row.Ema9); Assert.Null(row.Ema15); Assert.Equal(78200.22222222m, row.Ema100); Assert.Equal(DemoStrategySessionCandleObservationOrigin.RecoveryReplay, row.ObservationOrigin); Assert.Equal(observed, row.ObservedAtUtc);
    }

    private static DemoStrategySessionCandle Candle(int symbolId, DateTimeOffset close, DemoStrategySessionCandleObservationOrigin origin) => new() { DemoStrategySessionSymbolId = symbolId, OpenTimeUtc = close.AddMinutes(-3), CloseTimeUtc = close, Open = 1m, High = 2m, Low = .5m, Close = 1.5m, Volume = 1m, ObservationOrigin = origin, ObservedAtUtc = close };
    private static EmaBotDbContext NewDatabase() => new(new DbContextOptionsBuilder<EmaBotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
