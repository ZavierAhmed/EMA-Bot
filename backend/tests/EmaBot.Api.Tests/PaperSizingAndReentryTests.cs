using EmaBot.Api.Controllers;
using EmaBot.Api.Market;
using EmaBot.Api.Services;

namespace EmaBot.Api.Tests;

public sealed class PaperSizingAndReentryTests
{
    [Fact]
    public void FixedLotPreflight_AcceptsBtcMinimumAndRejectsEthBelowMinimum()
    {
        Assert.Null(PaperSessionsController.FixedLotsValidationMessage("BTCUSDm", .01m, .01m, 100m, .01m));

        var message = PaperSessionsController.FixedLotsValidationMessage("ETHUSDm", .01m, .10m, 2000m, .01m);

        Assert.NotNull(message);
        Assert.Contains("ETHUSDm", message);
        Assert.Contains("0.01", message);
        Assert.Contains("0.10", message);
    }

    [Theory]
    [InlineData(6, 6)]
    [InlineData(7, 7)]
    [InlineData(18, 18)]
    public void ReentryAgeBars_CountsCompletedCandlesAfterTheRegimeCrossover(int completedBars, int expected)
    {
        var start = DateTimeOffset.UnixEpoch;
        var candles = Enumerable.Range(0, completedBars + 1).Select(index =>
        {
            var open = start.AddMinutes(index * 3);
            return new Candle(open, open.AddMinutes(3).AddMilliseconds(-1), 100m, 101m, 99m, 100m, 1m, true);
        }).ToArray();

        Assert.Equal(expected, PaperTradingCoordinator.ReentryAgeBars(candles, candles[0].CloseTimeUtc, candles[^1].CloseTimeUtc));
    }

    [Fact]
    public void ExecutionSides_PreserveShortAskAndLongBidStops()
    {
        Assert.Equal(63026.39m, PaperTradingCoordinator.ExitExecutablePrice(EmaBot.Api.Strategy.SignalDirection.Short, 63016.39m, 63026.39m));
        Assert.Equal(63016.39m, PaperTradingCoordinator.ExitExecutablePrice(EmaBot.Api.Strategy.SignalDirection.Long, 63016.39m, 63026.39m));
    }
}
