using EmaBot.Api.Models;
using EmaBot.Api.Market;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Services;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Tests;

public sealed class Mt5NativeRiskPositionSizerTests
{
    [Theory]
    [InlineData(SignalDirection.Long, 100, 95)]
    [InlineData(SignalDirection.Short, 100, 105)]
    public async Task RiskPercent_UsesBrokerProfitAtExecutableEntryAndStop(SignalDirection direction, decimal entry, decimal stop)
    {
        var calculator = new Calculator();
        var result = await new Mt5NativeRiskPositionSizer(calculator).SizeAsync(Request(direction, entry, stop, 1000m, 1m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(.02m, result.Lots);
        Assert.Equal(10m, result.TargetRiskAmount);
        Assert.Equal(10m, result.ActualInitialRiskAmount);
        Assert.Equal(1m, result.ActualInitialRiskPercent);
        Assert.Equal(direction.ToString(), calculator.ProfitRequests[0].Direction);
        Assert.Equal(entry, calculator.ProfitRequests[0].OpenPrice);
        Assert.Equal(stop, calculator.ProfitRequests[0].ClosePrice);
    }

    [Fact]
    public async Task RiskPercent_NormalizesDownAndNeverExceedsBudget()
    {
        var result = await new Mt5NativeRiskPositionSizer(new Calculator()).SizeAsync(Request(SignalDirection.Long, 100m, 95m, 800m, 1m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(.01m, result.Lots);
        Assert.Equal(5m, result.ActualInitialRiskAmount);
        Assert.True(result.ActualInitialRiskAmount <= result.TargetRiskAmount);
    }

    [Fact]
    public async Task RiskPercent_BelowMinimumVolumeRejectsWithoutRoundingUp()
    {
        var result = await new Mt5NativeRiskPositionSizer(new Calculator()).SizeAsync(Request(SignalDirection.Long, 100m, 95m, 400m, 1m), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Mt5NativeRiskSizingFailure.RiskBelowMinimumVolume, result.FailureReason);
        Assert.Equal(4m, result.TargetRiskAmount);
    }

    [Fact]
    public async Task RiskPercent_CapsAtBrokerMaximumAndReportsUnderRisk()
    {
        var result = await new Mt5NativeRiskPositionSizer(new Calculator()).SizeAsync(Request(SignalDirection.Long, 100m, 95m, 10_000m, 1m, volumeMax: .03m, volumeLimit: .02m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(.02m, result.Lots);
        Assert.Equal(10m, result.ActualInitialRiskAmount);
        Assert.Equal(100m, result.TargetRiskAmount);
    }

    [Fact]
    public async Task RiskPercent_AffordableRiskLotsWithExcessMarginRejectsWithoutShrinking()
    {
        var calculator = new Calculator { MarginPerLot = 100_000m };
        var result = await new Mt5NativeRiskPositionSizer(calculator).SizeAsync(Request(SignalDirection.Long, 100m, 95m, 1000m, 1m), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(Mt5NativeRiskSizingFailure.InsufficientMargin, result.FailureReason);
        Assert.Equal(.02m, calculator.MarginRequests.Single().VolumeLots);
    }

    [Fact]
    public async Task RiskPercent_ProfitCalculatorExceptionPreservesSafeDiagnosticContext()
    {
        var result = await new Mt5NativeRiskPositionSizer(new Calculator { ThrowOnProfit = true }).SizeAsync(Request(SignalDirection.Long, 100m, 95m, 1_000m, 1m), CancellationToken.None);

        Assert.Equal(Mt5NativeRiskSizingFailure.RiskCalculationUnavailable, result.FailureReason);
        Assert.Equal("CalculateProfit", result.Diagnostic?.Operation);
        Assert.Equal("BTCUSDm", result.Diagnostic?.BrokerSymbol);
        Assert.Equal("InvalidOperationException", result.Diagnostic?.ExceptionType);
        Assert.Equal(100m, result.Diagnostic?.EntryPrice);
        Assert.Equal(95m, result.Diagnostic?.InitialStopPrice);
    }

    [Fact]
    public async Task RiskPercent_MarginCalculatorExceptionPreservesSafeDiagnosticContext()
    {
        var result = await new Mt5NativeRiskPositionSizer(new Calculator { ThrowOnMargin = true }).SizeAsync(Request(SignalDirection.Long, 100m, 95m, 1_000m, 1m), CancellationToken.None);

        Assert.Equal(Mt5NativeRiskSizingFailure.MarginCalculationUnavailable, result.FailureReason);
        Assert.Equal("CalculateMargin", result.Diagnostic?.Operation);
        Assert.Equal(.02m, result.Diagnostic?.Lots);
        Assert.Equal("InvalidOperationException", result.Diagnostic?.ExceptionType);
    }

    [Fact]
    public async Task RiskPercent_ProviderFailurePreservesSafeProviderAndRootDiagnosticTypes()
    {
        var calculator = new Calculator { ProfitException = new MarketDataProviderException("MT5 trade calculation", MarketDataErrorKind.Unavailable, "The MT5 bridge is not connected.", new IOException("pipe reset")) };

        var result = await new Mt5NativeRiskPositionSizer(calculator).SizeAsync(Request(SignalDirection.Long, 100m, 95m, 1_000m, 1m), CancellationToken.None);

        Assert.Equal("MarketDataProviderException", result.Diagnostic?.ExceptionType);
        Assert.Equal("Unavailable", result.Diagnostic?.ProviderKind);
        Assert.Equal("IOException", result.Diagnostic?.RootExceptionType);
    }

    [Fact]
    public async Task RiskPercent_NonPositiveBrokerResultsAreAttributedToTheirOperations()
    {
        var profit = await new Mt5NativeRiskPositionSizer(new Calculator { ProfitOverride = 0m }).SizeAsync(Request(SignalDirection.Long, 100m, 95m, 1_000m, 1m), CancellationToken.None);
        var margin = await new Mt5NativeRiskPositionSizer(new Calculator { MarginPerLot = 0m }).SizeAsync(Request(SignalDirection.Long, 100m, 95m, 1_000m, 1m), CancellationToken.None);

        Assert.Equal(Mt5NativeRiskSizingFailure.RiskCalculationUnavailable, profit.FailureReason);
        Assert.Equal("CalculateProfit", profit.Diagnostic?.Operation);
        Assert.Equal(Mt5NativeRiskSizingFailure.MarginCalculationUnavailable, margin.FailureReason);
        Assert.Equal("CalculateMargin", margin.Diagnostic?.Operation);
    }

    private static Mt5NativeRiskSizingRequest Request(SignalDirection direction, decimal entry, decimal stop, decimal equity, decimal riskPercent, decimal volumeMax = 1m, decimal? volumeLimit = null)
        => new("BTCUSDm", direction, entry, stop, equity, riskPercent, .01m, volumeMax, .01m, volumeLimit);

    private sealed class Calculator : IMt5TradeCalculator
    {
        public decimal MarginPerLot { get; set; } = 100m;
        public bool ThrowOnProfit { get; set; }
        public bool ThrowOnMargin { get; set; }
        public Exception? ProfitException { get; set; }
        public decimal? ProfitOverride { get; set; }
        public List<Mt5CalculateProfitRequest> ProfitRequests { get; } = [];
        public List<Mt5CalculateMarginRequest> MarginRequests { get; } = [];
        public Task<Mt5MarginCalculationPayload> CalculateMarginAsync(Mt5CalculateMarginRequest request, CancellationToken token)
        {
            MarginRequests.Add(request);
            if (ThrowOnMargin) throw new InvalidOperationException("test margin transport failure");
            return Task.FromResult(new Mt5MarginCalculationPayload(request.BrokerSymbol, request.Direction, request.VolumeLots, request.OpenPrice, request.VolumeLots * MarginPerLot, "USD"));
        }
        public Task<Mt5ProfitCalculationPayload> CalculateProfitAsync(Mt5CalculateProfitRequest request, CancellationToken token)
        {
            ProfitRequests.Add(request);
            if (ProfitException is not null) throw ProfitException;
            if (ThrowOnProfit) throw new InvalidOperationException("test profit transport failure");
            var perLot = request.Direction == "Long" ? request.ClosePrice - request.OpenPrice : request.OpenPrice - request.ClosePrice;
            return Task.FromResult(new Mt5ProfitCalculationPayload(request.BrokerSymbol, request.Direction, request.VolumeLots, request.OpenPrice, request.ClosePrice, ProfitOverride ?? perLot * request.VolumeLots * 100m, "USD"));
        }
    }
}
