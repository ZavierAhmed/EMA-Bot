namespace EmaBot.Api.Execution;

/// <summary>Underlying exposure used for PnL, optionally paired with broker lots and contract size.</summary>
public sealed record PositionExposure(decimal Quantity, decimal QuoteNotional, decimal? Lots, decimal? ContractSize);

public sealed record InstrumentVolumeResult(
    decimal RequestedQuoteNotional,
    decimal RawLots,
    decimal Lots,
    decimal Quantity,
    decimal ActualQuoteNotional,
    bool WasClamped,
    string? RejectionReason)
{
    public bool IsAccepted => RejectionReason is null;
}
