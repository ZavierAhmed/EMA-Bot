namespace EmaBot.Api.Market;

public sealed record MarketQuote(string BrokerSymbol, DateTimeOffset TimeUtc, decimal Bid, decimal Ask)
{
    public decimal Spread => Ask - Bid;
}
