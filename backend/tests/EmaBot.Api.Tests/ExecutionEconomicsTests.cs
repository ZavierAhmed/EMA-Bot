using EmaBot.Api.Execution;
using EmaBot.Api.Market;
using EmaBot.Api.Models;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

public sealed class ExecutionEconomicsTests
{
    [Fact]
    public void VolumeCalculator_ConvertsOneUnitContractsFromNotional()
    {
        var result = InstrumentVolumeCalculator.Calculate(Spec(contractSize: 1m), 100m, 1000m);

        Assert.True(result.IsAccepted);
        Assert.Equal(10m, result.RawLots); Assert.Equal(10m, result.Lots); Assert.Equal(10m, result.Quantity); Assert.Equal(1000m, result.ActualQuoteNotional);
    }

    [Fact]
    public void VolumeCalculator_ConvertsContractLotsToUnderlyingQuantity()
    {
        var result = InstrumentVolumeCalculator.Calculate(Spec(contractSize: 100m), 50m, 1000m);

        Assert.True(result.IsAccepted);
        Assert.Equal(.20m, result.RawLots); Assert.Equal(.20m, result.Lots); Assert.Equal(20m, result.Quantity); Assert.Equal(1000m, result.ActualQuoteNotional);
    }

    [Theory]
    [InlineData(12.7, .01, .12)]
    [InlineData(74, .25, .50)]
    public void VolumeCalculator_NormalizesDownWithoutIncreasingExposure(decimal requestedNotional, decimal step, decimal expectedLots)
    {
        var result = InstrumentVolumeCalculator.Calculate(Spec(step: step), 100m, requestedNotional);

        Assert.True(result.IsAccepted);
        Assert.Equal(expectedLots, result.Lots);
        Assert.True(result.ActualQuoteNotional <= requestedNotional);
    }

    [Fact]
    public void VolumeCalculator_RejectsBelowMinimumWithoutIncreasingVolume()
    {
        var result = InstrumentVolumeCalculator.Calculate(Spec(min: .10m), 100m, 8m);

        Assert.False(result.IsAccepted); Assert.Equal(0m, result.Lots); Assert.Contains("below", result.RejectionReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VolumeCalculator_CapsAboveMaximumAndReportsActualExposure()
    {
        var result = InstrumentVolumeCalculator.Calculate(Spec(max: 5m), 100m, 624m);

        Assert.True(result.IsAccepted); Assert.True(result.WasClamped); Assert.Equal(5m, result.Lots); Assert.Equal(500m, result.ActualQuoteNotional);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void VolumeCalculator_RejectsInvalidPriceOrRequestedNotional(decimal entryPrice, decimal requestedNotional)
    {
        var result = InstrumentVolumeCalculator.Calculate(Spec(), entryPrice, requestedNotional);
        Assert.False(result.IsAccepted);
    }

    [Fact]
    public void VolumeCalculator_RejectsInvalidInstrumentSpecifications()
    {
        var valid = Spec();
        Assert.False(InstrumentVolumeCalculator.Calculate(valid with { ContractSize = 0m }, 100m, 100m).IsAccepted);
        Assert.False(InstrumentVolumeCalculator.Calculate(valid with { VolumeMin = 0m }, 100m, 100m).IsAccepted);
        Assert.False(InstrumentVolumeCalculator.Calculate(valid with { VolumeMax = .001m }, 100m, 100m).IsAccepted);
        Assert.False(InstrumentVolumeCalculator.Calculate(valid with { VolumeStep = 0m }, 100m, 100m).IsAccepted);
    }

    [Theory]
    [InlineData(SignalDirection.Long, 100, 110)]
    [InlineData(SignalDirection.Short, 100, 90)]
    public void PercentageCostModel_HasExactLegacyTradeMathParity(SignalDirection direction, decimal entry, decimal exit)
    {
        const decimal quantity = 2.5m; const decimal fee = .05m;
        var exposure = new PositionExposure(quantity, entry * quantity, null, null);
        var model = new PercentageNotionalCostModel(fee);

        Assert.Equal(TradeMath.Fee(entry, quantity, fee), model.EntryCost(entry, exposure));
        Assert.Equal(TradeMath.Fee(exit, quantity, fee), model.ExitCost(exit, exposure));
        Assert.Equal(TradeMath.ExpectedNetAtTarget(entry, exit, quantity, direction, fee), model.ExpectedNetPnl(entry, exit, exposure, direction));
        Assert.Equal(TradeMath.FeeBreakevenPrice(entry, direction, fee), model.BreakEvenExitPrice(entry, exposure, direction));
    }

    [Theory]
    [InlineData(SignalDirection.Long)]
    [InlineData(SignalDirection.Short)]
    public void PercentageCostModel_ZeroFeePreservesBreakevenAndTrailingParity(SignalDirection direction)
    {
        var exposure = new PositionExposure(1m, 100m, null, null);
        var model = new PercentageNotionalCostModel(0m);
        var calculated = direction == SignalDirection.Long ? 104m : 96m;

        Assert.Equal(0m, model.EntryCost(100m, exposure)); Assert.Equal(0m, model.ExitCost(100m, exposure));
        Assert.Equal(100m, model.BreakEvenExitPrice(100m, exposure, direction));
        Assert.Equal(calculated, TradeMath.FeeAwareTrailingStop(calculated, 100m, direction, 0m));
    }

    [Theory]
    [InlineData(SignalDirection.Long, 110, 497)]
    [InlineData(SignalDirection.Short, 90, 497)]
    public void PerLotCommissionModel_ChargesBothSidesAndCalculatesNetPnl(SignalDirection direction, decimal exit, decimal expectedNet)
    {
        var exposure = new PositionExposure(50m, 5000m, .50m, 100m);
        var model = new PerLotCommissionCostModel(3m);

        Assert.Equal(1.50m, model.EntryCost(100m, exposure)); Assert.Equal(1.50m, model.ExitCost(exit, exposure));
        Assert.Equal(expectedNet, model.ExpectedNetPnl(100m, exit, exposure, direction));
    }

    [Theory]
    [InlineData(SignalDirection.Long, 100.06)]
    [InlineData(SignalDirection.Short, 99.94)]
    public void PerLotCommissionModel_CalculatesExactBreakEven(SignalDirection direction, decimal expected)
    {
        var model = new PerLotCommissionCostModel(3m);
        var exposure = new PositionExposure(50m, 5000m, .50m, 100m);

        Assert.Equal(expected, model.BreakEvenExitPrice(100m, exposure, direction));
    }

    [Fact]
    public void PerLotCommissionModel_RequiresLots()
    {
        var model = new PerLotCommissionCostModel(3m);
        var exposure = new PositionExposure(50m, 5000m, null, null);

        Assert.Throws<InvalidOperationException>(() => model.EntryCost(100m, exposure));
    }

    [Theory]
    [InlineData(PositionSizingMode.FixedNotional)]
    [InlineData(PositionSizingMode.MarginPercent)]
    public void LegacyPositionSizingCalculator_HasExactTradeMathParity(PositionSizingMode mode)
    {
        var settings = new TradingSettings { PositionSizingMode = mode, FixedOrderSizeUsdt = 100m, MarginPerTradePercent = 10m, Leverage = 5m };
        var expected = TradeMath.CalculatePositionSize(settings, 1000m, 100m);
        var actual = LegacyPositionSizingCalculator.Calculate(settings, 1000m, 100m);

        Assert.Equal(expected.AccountEquityAtEntryUsdt, actual.AccountEquityAtEntryUsdt);
        Assert.Equal(expected.MarginUsedUsdt, actual.MarginUsedUsdt);
        Assert.Equal(expected.Leverage, actual.Leverage);
        Assert.Equal(expected.NotionalUsdt, actual.Exposure.QuoteNotional);
        Assert.Equal(expected.Quantity, actual.Exposure.Quantity);
    }

    [Fact]
    public void LegacyBacktest_DefaultEconomicsPreserveQuantityFeesAndPnl()
    {
        var candles = Enumerable.Range(0, 8).Select(index =>
        {
            var time = DateTimeOffset.UnixEpoch.AddMinutes(index);
            return new Candle(time, time.AddMinutes(1).AddMilliseconds(-1), 100m, 101m, 99m, 100m, 1m, true);
        }).ToArray();
        candles[0] = candles[0] with { Low = 90m };
        candles[6] = candles[6] with { High = 120m, Low = 95m };
        var snapshot = new IndicatorSnapshot(candles[5].CloseTimeUtc, 100m, 1m, 1m, null, null, GapState.Unchanged, TrendDirection.Neutral);
        var calculation = new BacktestEngine(new EmaSignalEngine()).RunWithEvents(candles, new TradingSettings { RiskReward = 2m, FixedOrderSizeUsdt = 100m, FeePercentPerSide = .1m, MaxStopDistancePercent = 0m }, [new StrategyEvent(candles[5].CloseTimeUtc, SignalDirection.Long, SignalStatus.BullishCrossover, snapshot), new StrategyEvent(candles[5].CloseTimeUtc, SignalDirection.Long, SignalStatus.LongSignal, snapshot)]);
        var trade = Assert.Single(calculation.Trades);

        Assert.Equal(1m, trade.Quantity); Assert.Equal(100m, trade.EntryNotionalUsdt); Assert.Equal(.1m, trade.EntryFeeUsdt); Assert.Equal(.12m, trade.ExitFeeUsdt); Assert.Equal(.22m, trade.TotalFeesUsdt); Assert.Equal(20m, trade.GrossPnlUsdt); Assert.Equal(19.78m, trade.NetPnlUsdt); Assert.Equal(BacktestExitReason.TakeProfit, trade.ExitReason);
    }

    private static InstrumentSpec Spec(decimal contractSize = 1m, decimal min = .01m, decimal max = 100m, decimal step = .01m)
        => new("Synthetic", "SYN", "Synthetic", AssetClass.Unknown, 2, .01m, contractSize, min, max, step, null, null, null);
}
