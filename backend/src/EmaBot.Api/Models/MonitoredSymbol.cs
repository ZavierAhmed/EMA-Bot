namespace EmaBot.Api.Models;

public sealed class MonitoredSymbol
{
    public int Id { get; set; }
    public MarketDataSource Source { get; set; }
    // Exact provider identity. MT5 broker suffixes and casing are intentional.
    public required string Symbol { get; set; }
    public string? DisplayName { get; set; }
    public string? BaseAsset { get; set; }
    public string? QuoteAsset { get; set; }
    // Null means intentionally not configured; zero is an explicit commission-free confirmation.
    public decimal? PaperCommissionPerLotPerSide { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
