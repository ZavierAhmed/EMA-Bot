using EmaBot.Api.Models;
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
        async Task<decimal?> LossAsync(decimal lots)
        {
            try
            {
                calls++;
                return decimal.Abs((await calculator.CalculateProfitAsync(new Mt5CalculateProfitRequest(request.BrokerSymbol, request.Direction.ToString(), lots, request.EntryPrice, request.InitialStopPrice), token)).Profit);
            }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }

        var referenceLoss = await LossAsync(request.VolumeMin);
        if (referenceLoss is not > 0m) return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.RiskCalculationUnavailable, calls);
        if (referenceLoss.Value > targetRisk + RiskTolerance)
            return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.RiskBelowMinimumVolume, calls, targetRisk);

        var candidate = NormalizeDown(Math.Min(maximum, request.VolumeMin * targetRisk / referenceLoss.Value), request.VolumeMin, request.VolumeStep);
        for (var attempt = 0; attempt < MaxRiskRevalidationAttempts && candidate >= request.VolumeMin; attempt++)
        {
            var actualRisk = await LossAsync(candidate);
            if (actualRisk is null) return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.RiskCalculationUnavailable, calls, targetRisk);
            if (actualRisk.Value <= targetRisk + RiskTolerance)
            {
                decimal margin;
                try
                {
                    calls++;
                    margin = (await calculator.CalculateMarginAsync(new Mt5CalculateMarginRequest(request.BrokerSymbol, request.Direction.ToString(), candidate, request.EntryPrice), token)).RequiredMargin;
                }
                catch (OperationCanceledException) { throw; }
                catch { return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.MarginCalculationUnavailable, calls, targetRisk); }
                if (margin <= 0m) return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.MarginCalculationUnavailable, calls, targetRisk);
                if (margin > request.Equity) return Mt5NativeRiskSizingResult.Failure(Mt5NativeRiskSizingFailure.InsufficientMargin, calls, targetRisk, actualRisk);
                return Mt5NativeRiskSizingResult.Success(candidate, margin, request.Equity, request.RiskPercent, targetRisk, actualRisk.Value, calls);
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
public sealed record Mt5NativeRiskSizingResult(decimal? Lots, decimal? RequiredMargin, decimal Equity, decimal? TargetRiskPercent, decimal? TargetRiskAmount, decimal? ActualInitialRiskAmount, int CalculationCalls, Mt5NativeRiskSizingFailure? FailureReason)
{
    public bool IsSuccess => Lots is not null;
    public decimal? ActualInitialRiskPercent => ActualInitialRiskAmount is not null && Equity > 0m ? ActualInitialRiskAmount / Equity * 100m : null;
    public static Mt5NativeRiskSizingResult Success(decimal lots, decimal margin, decimal equity, decimal targetPercent, decimal targetAmount, decimal actualAmount, int calls) => new(lots, margin, equity, targetPercent, targetAmount, actualAmount, calls, null);
    public static Mt5NativeRiskSizingResult Failure(Mt5NativeRiskSizingFailure reason, int calls = 0, decimal? targetAmount = null, decimal? actualAmount = null) => new(null, null, 0m, null, targetAmount, actualAmount, calls, reason);
}
