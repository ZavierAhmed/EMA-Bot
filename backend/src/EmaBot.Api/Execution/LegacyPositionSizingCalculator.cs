using EmaBot.Api.Models;
using EmaBot.Api.Strategy;

namespace EmaBot.Api.Execution;

public sealed record LegacyPositionSizingResult(decimal AccountEquityAtEntryUsdt, decimal MarginUsedUsdt, decimal Leverage, PositionExposure Exposure);

public static class LegacyPositionSizingCalculator
{
    public static LegacyPositionSizingResult Calculate(TradingSettings settings, decimal accountEquity, decimal entryPrice)
    {
        var margin = settings.PositionSizingMode == PositionSizingMode.MarginPercent
            ? accountEquity * settings.MarginPerTradePercent / 100m
            : settings.FixedOrderSizeUsdt;
        var leverage = settings.PositionSizingMode == PositionSizingMode.MarginPercent ? settings.Leverage : 1m;
        var notional = settings.PositionSizingMode == PositionSizingMode.MarginPercent ? margin * leverage : settings.FixedOrderSizeUsdt;
        return new LegacyPositionSizingResult(accountEquity, settings.PositionSizingMode == PositionSizingMode.MarginPercent ? margin : 0m, leverage, new PositionExposure(notional / entryPrice, notional, null, null));
    }
}
