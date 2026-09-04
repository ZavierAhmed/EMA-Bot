using EmaBot.Api.Models;
using EmaBot.Api.Market;
using EmaBot.Api.Mt5Bridge;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Services;

// This deliberately delegates both stop-risk and affordability to the MT5 bridge.
// It contains no contract-size, tick-value, price-distance, or synthetic-leverage calculation.
public sealed class Mt5NativeRiskPositionSizer(IMt5TradeCalculator calculator)
{
    private const decimal RiskTolerance = 0.00000001m;
    private const int MaxRiskRevalidationAttempts = 8;

    public async Task<Mt5NativeRiskSizingResult> SizeAsync(Mt5NativeRiskSizingRequest request, CancellationToken token)
    {
        if (request.Equity <= 0m || request.RiskPercent <= 0m || request.RiskPercent > 100m)
            return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.InvalidRiskConfiguration);
        if (request.VolumeMin <= 0m || request.VolumeMax < request.VolumeMin || request.VolumeStep <= 0m)
            return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.InvalidVolume);

        var maximum = Math.Min(request.VolumeMax, request.VolumeLimit is > 0m ? request.VolumeLimit.Value : request.VolumeMax);
        if (maximum < request.VolumeMin) return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.InvalidVolume);

        var targetRisk = request.Equity * request.RiskPercent / 100m;
        if (targetRisk <= 0m) return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.InvalidRiskConfiguration);
        var calls = 0;
        Mt5NativeRiskSizingDiagnostic Diagnostic(string operation, decimal lots, Exception? exception = null, string? detail = null)
            => new(operation, request.BrokerSymbol, request.Direction, request.EntryPrice, request.InitialStopPrice, lots, request.Equity, request.RiskPercent, targetRisk, exception?.GetType().Name, exception?.Message ?? detail, (exception as MarketDataProviderException)?.Kind.ToString(), exception?.InnerException?.GetType().Name);
        async Task<(decimal? Loss, Mt5NativeRiskSizingDiagnostic? Diagnostic)> LossAsync(decimal lots)
        {
            try
            {
                calls++;
                var loss = decimal.Abs((await calculator.CalculateProfitAsync(new Mt5CalculateProfitRequest(request.BrokerSymbol, request.Direction.ToString(), lots, request.EntryPrice, request.InitialStopPrice), token)).Profit);
                return loss > 0m ? (loss, null) : (null, Diagnostic("CalculateProfit", lots, detail: "MT5 CalculateProfit returned a non-positive stop-risk value."));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) { return (null, Diagnostic("CalculateProfit", lots, exception)); }
        }

        var reference = await LossAsync(request.VolumeMin);
        if (reference.Loss is not > 0m) return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.RiskCalculationUnavailable, calls, targetRisk, diagnostic: reference.Diagnostic);
        if (reference.Loss.Value > targetRisk + RiskTolerance)
            return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.RiskBelowMinimumVolume, calls, targetRisk);

        var candidate = NormalizeDown(Math.Min(maximum, request.VolumeMin * targetRisk / reference.Loss.Value), request.VolumeMin, request.VolumeStep);
        for (var attempt = 0; attempt < MaxRiskRevalidationAttempts && candidate >= request.VolumeMin; attempt++)
        {
            var actual = await LossAsync(candidate);
            if (actual.Loss is null) return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.RiskCalculationUnavailable, calls, targetRisk, diagnostic: actual.Diagnostic);
            if (actual.Loss.Value <= targetRisk + RiskTolerance)
            {
                decimal margin;
                try
                {
                    calls++;
                    margin = (await calculator.CalculateMarginAsync(new Mt5CalculateMarginRequest(request.BrokerSymbol, request.Direction.ToString(), candidate, request.EntryPrice), token)).RequiredMargin;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.MarginCalculationUnavailable, calls, targetRisk, diagnostic: Diagnostic("CalculateMargin", candidate, exception)); }
                if (margin <= 0m) return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.MarginCalculationUnavailable, calls, targetRisk, diagnostic: Diagnostic("CalculateMargin", candidate, detail: "MT5 CalculateMargin returned a non-positive margin value."));
                if (margin > request.Equity) return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.InsufficientMargin, calls, targetRisk, actual.Loss);
                return Mt5NativeRiskSizingResult.Success(candidate, margin, request.Equity, request.RiskPercent, targetRisk, actual.Loss.Value, calls);
            }
            candidate = NormalizeDown(candidate - request.VolumeStep, request.VolumeMin, request.VolumeStep);
        }
        return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.RiskCannotBeSafelySized, calls, targetRisk);
    }

    private static decimal NormalizeDown(decimal lots, decimal minimum, decimal step)
        => minimum + decimal.Floor((lots - minimum) / step) * step;
}

public sealed record Mt5NativeRiskSizingRequest(string BrokerSymbol, SignalDirection Direction, decimal EntryPrice, decimal InitialStopPrice, decimal Equity, decimal RiskPercent, decimal VolumeMin, decimal VolumeMax, decimal VolumeStep, decimal? VolumeLimit);
public enum Mt5NativeRiskSizingFailure { InvalidRiskConfiguration, InvalidVolume, RiskBelowMinimumVolume, RiskCannotBeSafelySized, RiskCalculationUnavailable, MarginCalculationUnavailable, InsufficientMargin }
public sealed record Mt5NativeRiskSizingDiagnostic(string Operation, string BrokerSymbol, SignalDirection Direction, decimal EntryPrice, decimal InitialStopPrice, decimal Lots, decimal Equity, decimal RiskPercent, decimal TargetRiskAmount, string? ExceptionType, string? SafeMessage, string? ProviderKind = null, string? RootExceptionType = null);
public sealed class Mt5NativeEconomicsUnavailableException(Mt5NativeRiskSizingFailure failureReason, Mt5NativeRiskSizingDiagnostic? diagnostic) : InvalidOperationException($"MT5 RiskPercent {failureReason}.")
{ public Mt5NativeRiskSizingFailure FailureReason { get; } = failureReason; public Mt5NativeRiskSizingDiagnostic? Diagnostic { get; } = diagnostic; }
public sealed class Mt5RiskPercentConfigurationException : InvalidOperationException { public Mt5RiskPercentConfigurationException() : base("MT5 RiskPercent configuration is invalid.") { } }
public sealed record Mt5NativeRiskSizingResult(decimal? Lots, decimal? RequiredMargin, decimal Equity, decimal? TargetRiskPercent, decimal? TargetRiskAmount, decimal? ActualInitialRiskAmount, int CalculationCalls, Mt5NativeRiskSizingFailure? FailureReason, Mt5NativeRiskSizingDiagnostic? Diagnostic = null)
{
    public bool IsSuccess => Lots is not null;
    public decimal? ActualInitialRiskPercent => ActualInitialRiskAmount is not null && Equity > 0m ? ActualInitialRiskAmount / Equity * 100m : null;
    public static Mt5NativeRiskSizingResult Success(decimal lots, decimal margin, decimal equity, decimal targetPercent, decimal targetAmount, decimal actualAmount, int calls) => new(lots, margin, equity, targetPercent, targetAmount, actualAmount, calls, null);
    public static Mt5NativeRiskSizingResult Failure(Mt5NativeRiskSizingFailure reason, int calls = 0, decimal? targetAmount = null, decimal? actualAmount = null, Mt5NativeRiskSizingDiagnostic? diagnostic = null) => new(null, null, 0m, null, targetAmount, actualAmount, calls, reason, diagnostic);
}
