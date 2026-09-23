using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Tests;

public sealed class GridNumericRoundTripTests
{
    private static readonly Mt5CalculateProfitRequest Profit = new("BTCUSDm", "Long", .123456789123m, 100.123456789123m, 90.123456789123m);
    private static readonly Mt5CalculateMarginRequest Margin = new(Profit.BrokerSymbol, Profit.Direction, Profit.VolumeLots, Profit.OpenPrice);

    [Theory]
    [InlineData("exact")] [InlineData("open")] [InlineData("close")] [InlineData("volume")]
    public async Task ProfitAcceptsExactAndTenDecimalEchoAndKeepsOriginalKey(string field)
    {
        var calculator = new Native { Field = field }; var economics = new GridHistoricalEconomics(calculator, "USD");
        var result = await economics.CalculateProfitAsync(Profit, default);
        Assert.Equal(-1m, economics.Profits[Profit]);
        Assert.Same(Profit, Assert.Single(economics.Profits).Key);
        if (field != "exact") Assert.False(economics.Profits.ContainsKey(new(result.BrokerSymbol, result.Direction, result.VolumeLots, result.OpenPrice, result.ClosePrice)));
    }

    [Theory]
    [InlineData("exact")] [InlineData("open")] [InlineData("volume")]
    public async Task MarginAcceptsExactAndTenDecimalEchoAndKeepsOriginalKey(string field)
    {
        var economics = new GridHistoricalEconomics(new Native { Field = field }, "USD");
        var result = await economics.CalculateMarginAsync(Margin, default);
        Assert.Equal(1m, economics.Margins[Margin]);
        Assert.Same(Margin, Assert.Single(economics.Margins).Key);
        if (field != "exact") Assert.False(economics.Margins.ContainsKey(new(result.BrokerSymbol, result.Direction, result.VolumeLots, result.OpenPrice)));
    }

    [Theory]
    [InlineData("symbol")] [InlineData("direction")] [InlineData("currency")]
    [InlineData("open")] [InlineData("close")] [InlineData("volume")]
    public async Task ProfitRejectsMismatchedEvidence(string field)
    {
        var economics = new GridHistoricalEconomics(new Native { Field = field, Invalid = true }, "USD");
        await Assert.ThrowsAsync<InvalidOperationException>(() => economics.CalculateProfitAsync(Profit, default));
        Assert.Empty(economics.Profits);
    }

    [Theory]
    [InlineData("symbol")] [InlineData("direction")] [InlineData("currency")]
    [InlineData("open")] [InlineData("volume")] [InlineData("zero")] [InlineData("negative")]
    public async Task MarginRejectsMismatchedOrNonPositiveEvidence(string field)
    {
        var economics = new GridHistoricalEconomics(new Native { Field = field, Invalid = true }, "USD");
        await Assert.ThrowsAsync<InvalidOperationException>(() => economics.CalculateMarginAsync(Margin, default));
        Assert.Empty(economics.Margins);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task RoundedSizingEvidenceSucceedsForRiskAndMargin(bool invalid, bool marginOnly)
    {
        var economics = new GridHistoricalEconomics(new Native { Field = "open", Invalid = invalid, MarginOnly = marginOnly }, "USD");
        var qualification = new GridRangeQualification(new(DateTimeOffset.UnixEpoch, 104.123456789123m, 96.123456789123m, 100.123456789123m, 4m, 0m, 50), null);
        var result = await new GridRangeRiskSizer(economics).SizeAsync("BTCUSDm", qualification, new(), new(1000m, 1000m, .01m, 1m, .01m));
        if (invalid) Assert.Equal(marginOnly ? GridCycleDiagnostics.MarginCalculationUnavailable : GridCycleDiagnostics.RiskCalculationUnavailable, result.Failure);
        else { Assert.True(result.IsSuccess); Assert.Null(result.Diagnostic); Assert.NotEmpty(economics.Profits); Assert.NotEmpty(economics.Margins); }
    }

    [Theory]
    [InlineData(1)] [InlineData(-1)]
    public async Task AbsoluteToleranceBoundaryIsInclusiveAndScaleIndependent(int sign)
    {
        foreach (var price in new[] { .001m, 100000m })
        foreach (var delta in new[] { .0000000001m, .000000000101m })
        {
            var economics = new GridHistoricalEconomics(new Native { Offset = sign * delta }, "USD");
            var profit = Profit with { OpenPrice = price }; var margin = Margin with { OpenPrice = price };
            if (delta == .0000000001m)
            {
                await economics.CalculateProfitAsync(profit, default);
                await economics.CalculateMarginAsync(margin, default);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => economics.CalculateProfitAsync(profit, default));
                await Assert.ThrowsAsync<InvalidOperationException>(() => economics.CalculateMarginAsync(margin, default));
                Assert.Empty(economics.Profits); Assert.Empty(economics.Margins);
            }
        }
    }

    private sealed class Native : IMt5TradeCalculator
    {
        public string Field { get; init; } = "exact";
        public bool Invalid { get; init; }
        public bool MarginOnly { get; init; }
        public decimal? Offset { get; init; }
        private decimal Echo(string field, decimal value) => Offset is { } offset && field == "open" ? value + offset
            : Field != field ? value : Invalid ? value + .00000000011m : decimal.Round(value, 10);
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest r, CancellationToken token) => Task.FromResult(new Mt5ProfitCalculationPayload(
            Field == "symbol" ? "btcusdm" : r.BrokerSymbol, Field == "direction" ? "Short" : r.Direction,
            MarginOnly ? r.VolumeLots : Echo("volume", r.VolumeLots), MarginOnly ? r.OpenPrice : Echo("open", r.OpenPrice), MarginOnly ? r.ClosePrice : Echo("close", r.ClosePrice), -1m, Field == "currency" ? "usd" : "USD"));
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest r, CancellationToken token) => Task.FromResult(new Mt5MarginCalculationPayload(
            Field == "symbol" ? "btcusdm" : r.BrokerSymbol, Field == "direction" ? "Short" : r.Direction,
            Echo("volume", r.VolumeLots), Echo("open", r.OpenPrice), Field == "zero" ? 0m : Field == "negative" ? -1m : 1m, Field == "currency" ? "usd" : "USD"));
    }
}
