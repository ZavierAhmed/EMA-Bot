namespace EmaBot.Api.Models;

public sealed class MonitoredSymbol
{
    public int Id { get; set; }
    public required string Symbol { get; set; }
    public required string BaseAsset { get; set; }
    public required string QuoteAsset { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
