namespace EmaBot.Api.Market;

public sealed record MarketQuote
{
    public MarketQuote(string brokerSymbol, DateTimeOffset timeUtc, decimal bid, decimal ask, decimal? last = null, decimal? volume = null)
    {
        if (bid <= 0m) throw new ArgumentOutOfRangeException(nameof(bid), "Bid must be greater than zero.");
        if (ask <= 0m) throw new ArgumentOutOfRangeException(nameof(ask), "Ask must be greater than zero.");
        if (ask < bid) throw new ArgumentException("Ask must be greater than or equal to bid.", nameof(ask));
        BrokerSymbol = brokerSymbol;
        TimeUtc = timeUtc;
        Bid = bid;
        Ask = ask;
        Last = last;
        Volume = volume;
    }

    public string BrokerSymbol { get; }
    public DateTimeOffset TimeUtc { get; }
    public decimal Bid { get; }
    public decimal Ask { get; }
    public decimal? Last { get; }
    public decimal? Volume { get; }
    public decimal Spread => Ask - Bid;
}
