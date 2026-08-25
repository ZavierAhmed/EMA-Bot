namespace EmaBot.Api.Market;

public enum AssetClass { Unknown, Forex, Crypto, Commodity, Index, Stock }
public enum HistoricalChartMode { Unknown, Bid, Last }

public sealed record InstrumentSpec(
    string Broker,
    string BrokerSymbol,
    string DisplaySymbol,
    AssetClass AssetClass,
    int Digits,
    decimal PointSize,
    decimal ContractSize,
    decimal VolumeMin,
    decimal VolumeMax,
    decimal VolumeStep,
    string? CurrencyBase,
    string? CurrencyProfit,
    string? CurrencyMargin,
    decimal? TickSize = null,
    decimal? TickValueProfit = null,
    decimal? TickValueLoss = null,
    decimal? VolumeLimit = null,
    int? StopsLevelPoints = null,
    int? FreezeLevelPoints = null,
    HistoricalChartMode HistoricalChartMode = HistoricalChartMode.Unknown);
