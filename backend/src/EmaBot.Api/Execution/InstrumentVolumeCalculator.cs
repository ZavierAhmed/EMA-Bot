using EmaBot.Api.Market;

namespace EmaBot.Api.Execution;

public static class InstrumentVolumeCalculator
{
    public static InstrumentVolumeResult Calculate(InstrumentSpec spec, decimal entryPrice, decimal requestedQuoteNotional)
    {
        if (entryPrice <= 0m) return Reject(requestedQuoteNotional, "Entry price must be greater than zero.");
        if (requestedQuoteNotional <= 0m) return Reject(requestedQuoteNotional, "Requested quote notional must be greater than zero.");
        if (spec.ContractSize <= 0m) return Reject(requestedQuoteNotional, "Instrument contract size must be greater than zero.");
        if (spec.VolumeMin <= 0m) return Reject(requestedQuoteNotional, "Instrument minimum volume must be greater than zero.");
        if (spec.VolumeMax < spec.VolumeMin) return Reject(requestedQuoteNotional, "Instrument maximum volume must be at least the minimum volume.");
        if (spec.VolumeStep <= 0m) return Reject(requestedQuoteNotional, "Instrument volume step must be greater than zero.");

        var rawLots = requestedQuoteNotional / (entryPrice * spec.ContractSize);
        if (rawLots < spec.VolumeMin) return Reject(requestedQuoteNotional, "Requested volume is below the instrument minimum volume.", rawLots);

        var cappedLots = decimal.Min(rawLots, spec.VolumeMax);
        var lots = decimal.Floor(cappedLots / spec.VolumeStep) * spec.VolumeStep;
        if (lots < spec.VolumeMin) return Reject(requestedQuoteNotional, "Normalized volume is below the instrument minimum volume.", rawLots);

        var quantity = lots * spec.ContractSize;
        return new InstrumentVolumeResult(requestedQuoteNotional, rawLots, lots, quantity, entryPrice * quantity, rawLots > spec.VolumeMax, null);
    }

    private static InstrumentVolumeResult Reject(decimal requestedQuoteNotional, string reason, decimal rawLots = 0m)
        => new(requestedQuoteNotional, rawLots, 0m, 0m, 0m, false, reason);
}
