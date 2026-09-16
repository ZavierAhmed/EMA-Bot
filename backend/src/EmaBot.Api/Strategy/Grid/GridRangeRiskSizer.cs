using EmaBot.Api.Mt5Bridge;

namespace EmaBot.Api.Strategy.Grid;

public sealed class GridRangeRiskSizer(IMt5TradeCalculator calculator)
{
    // No retry loop: transient retry belongs exclusively to IMt5TradeCalculator.
    // Only allowed candidate directions are preflighted (default: both), so
    // compare each basket separately, never sum long and short risk/margin together.
    public async Task<GridRangeSizing> SizeAsync(string symbol, GridRangeQualification qualification,
        GridRangeSettings settings, GridRiskAccount account, CancellationToken token = default)
    {
        GridRangeSizing Fail(GridCycleDiagnostics failure, GridRiskDiagnostic? diagnostic = null)
            => new(0m, 0m, null, null, null, null, failure, diagnostic, account.AllowedDirections);
        if (string.IsNullOrWhiteSpace(symbol) || !settings.IsValid || !qualification.IsQualified
            || !GridRangeIndicators.Evaluate(qualification.Snapshot).IsQualified
            || account.Equity <= 0m || account.FreeMargin < 0m || account.VolumeMin <= 0m
            || account.VolumeStep <= 0m || account.VolumeMax < account.VolumeMin
            || account.VolumeLimit < 0m || account.ExistingLongVolume < 0m || account.ExistingShortVolume < 0m
            || account.AllowedDirections == GridAllowedDirections.None
            || (account.AllowedDirections & ~GridAllowedDirections.Both) != 0)
            return Fail(GridCycleDiagnostics.RiskCannotBeSafelySized);
        var allowLong = (account.AllowedDirections & GridAllowedDirections.Long) != 0;
        var allowShort = (account.AllowedDirections & GridAllowedDirections.Short) != 0;
        var target = account.Equity * settings.GridBasketRiskPercent / 100m;
        var maximum = account.VolumeMax;
        if (account.VolumeLimit is > 0m)
            maximum = Math.Min(maximum, (account.VolumeLimit.Value - Math.Max(allowLong ? account.ExistingLongVolume : 0m,
                allowShort ? account.ExistingShortVolume : 0m)) / settings.LevelCount);
        if (maximum < account.VolumeMin) return Fail(GridCycleDiagnostics.RiskCannotBeSafelySized);
        string operation = "CalculateProfit";
        GridBasketDirection direction = GridBasketDirection.Long;
        decimal lots = account.VolumeMin, entry = 0m, stop = 0m;
        async Task<decimal> TotalAsync(GridBasketDirection side, decimal volume, bool margin)
        {
            direction = side; lots = volume; operation = margin ? "CalculateMargin" : "CalculateProfit";
            stop = qualification.Anchor + (side == GridBasketDirection.Long ? -6m : 6m) * qualification.Spacing;
            decimal total = 0m;
            for (var n = 1; n <= settings.LevelCount; n++)
            {
                token.ThrowIfCancellationRequested();
                entry = qualification.Anchor + (side == GridBasketDirection.Long ? -n : n) * qualification.Spacing;
                if (margin)
                {
                    var value = (await calculator.CalculateMarginAsync(new(symbol, side.ToString(), volume, entry), token)).RequiredMargin;
                    if (value <= 0m) throw new InvalidOperationException("Non-positive MT5 required margin.");
                    total += value;
                }
                else
                {
                    var value = (await calculator.CalculateProfitAsync(new(symbol, side.ToString(), volume, entry, stop), token)).Profit;
                    if (value >= 0m) throw new InvalidOperationException("MT5 emergency-stop calculation must return a loss.");
                    total += decimal.Abs(value);
                }
            }
            return total;
        }
        try
        {
            decimal? longRisk = allowLong ? await TotalAsync(GridBasketDirection.Long, account.VolumeMin, false) : null;
            decimal? shortRisk = allowShort ? await TotalAsync(GridBasketDirection.Short, account.VolumeMin, false) : null;
            // Null denotes an uncalculated side. Zero is only a local max identity;
            // it is never exposed as prohibited-side economics evidence.
            var referenceRisk = Math.Max(longRisk ?? 0m, shortRisk ?? 0m);
            if (referenceRisk > target)
                return Fail(GridCycleDiagnostics.RiskBelowMinimumVolume) with { TargetRiskAmount = target };
            // MT5 volume lattice follows the existing native sizer: minimum + N * step.
            // Estimate from native minimum-volume loss, normalize DOWN, then validate
            // each actual leg with MT5. Never accept an estimate as calculated risk.
            var estimate = Math.Min(maximum, account.VolumeMin * (target / referenceRisk));
            var candidate = account.VolumeMin + decimal.Floor((estimate - account.VolumeMin) / account.VolumeStep) * account.VolumeStep;
            longRisk = allowLong ? await TotalAsync(GridBasketDirection.Long, candidate, false) : null;
            shortRisk = allowShort ? await TotalAsync(GridBasketDirection.Short, candidate, false) : null;
            if (Math.Max(longRisk ?? 0m, shortRisk ?? 0m) > target)
                return Fail(GridCycleDiagnostics.RiskCannotBeSafelySized) with { TargetRiskAmount = target };
            decimal? longMargin = allowLong ? await TotalAsync(GridBasketDirection.Long, candidate, true) : null;
            decimal? shortMargin = allowShort ? await TotalAsync(GridBasketDirection.Short, candidate, true) : null;
            var failure = Math.Max(longMargin ?? 0m, shortMargin ?? 0m) > Math.Min(account.Equity, account.FreeMargin)
                ? GridCycleDiagnostics.InsufficientMargin : (GridCycleDiagnostics?)null;
            // Commission is intentionally absent: target is initial price-risk only.
            return new(candidate, target, longRisk, shortRisk, longMargin, shortMargin, failure, AllowedDirections: account.AllowedDirections);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return Fail(operation == "CalculateMargin" ? GridCycleDiagnostics.MarginCalculationUnavailable : GridCycleDiagnostics.RiskCalculationUnavailable,
                new(operation, symbol, direction, lots, entry, stop, exception.GetType().Name + ": " + exception.Message));
        }
    }
}
