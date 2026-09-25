namespace EmaBot.Api.Models;

// Dedicated Grid persistence; no inheritance from EMA run/trade entities.
public sealed class GridBacktestRun
{
    public int Id { get; set; }
    public string StrategyId { get; set; } = string.Empty;
    public string MarketDataSource { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string BrokerSymbol { get; set; } = string.Empty;
    public string Interval { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AccountCurrency { get; set; } = string.Empty;
    public string HistoricalSpreadModel { get; set; } = string.Empty;
    public string HistoricalChartMode { get; set; } = string.Empty;
    public string TradeMode { get; set; } = string.Empty;
    public string? FailureMessage { get; set; }
    public DateTimeOffset RequestedStartUtc { get; set; }
    public DateTimeOffset RequestedEndUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset CompletedAtUtc { get; set; }
    public DateTimeOffset? ActualStartUtc { get; set; }
    public DateTimeOffset? ActualEndUtc { get; set; }
    public decimal StartingBalance { get; set; }
    public decimal EndingBalance { get; set; }
    public decimal GridBasketRiskPercent { get; set; }
    public decimal AdxThreshold { get; set; }
    public decimal AtrSpacingMultiplier { get; set; }
    public decimal CommissionPerLotPerSide { get; set; }
    public decimal ContractSize { get; set; }
    public decimal VolumeMin { get; set; }
    public decimal VolumeMax { get; set; }
    public decimal VolumeStep { get; set; }
    public decimal PointSize { get; set; }
    public decimal GrossPnl { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal NetPnl { get; set; }
    public decimal AverageNetPnl { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal? VolumeLimit { get; set; }
    public decimal? TickSize { get; set; }
    public decimal? TickValueProfit { get; set; }
    public decimal? TickValueLoss { get; set; }
    public decimal? GrossProfitFactor { get; set; }
    public decimal? NetProfitFactor { get; set; }
    public int? StopsLevelPoints { get; set; }
    public int LevelCount { get; set; }
    public int CooldownBars { get; set; }
    public int RangeLookback { get; set; }
    public int AtrPeriod { get; set; }
    public int AdxPeriod { get; set; }
    public int ReportingCandleCount { get; set; }
    public int WarmupCandleCount { get; set; }
    public int BasketCount { get; set; }
    public int WinningBaskets { get; set; }
    public int LosingBaskets { get; set; }
    public int BreakEvenBaskets { get; set; }
    public int LongBaskets { get; set; }
    public int ShortBaskets { get; set; }
    public int QualifiedCycles { get; set; }
    public int RejectedQualificationCount { get; set; }
    public int AmbiguousFirstSideCount { get; set; }
    public int NoFillCyclesAtEndOfData { get; set; }
    public int TradeModeBlockedCount { get; set; }
    public int RiskBelowMinimumVolumeCount { get; set; }
    public int RiskCannotBeSafelySizedCount { get; set; }
    public int RiskCalculationUnavailableCount { get; set; }
    public int MarginCalculationUnavailableCount { get; set; }
    public int InsufficientMarginCount { get; set; }
    public int EconomicsCallCount { get; set; }
    public long EconomicsElapsedMilliseconds { get; set; }
    public List<GridBacktestCycle> Cycles { get; set; } = [];
    public List<GridBacktestEvent> Events { get; set; } = [];
    public List<GridBacktestDiagnostic> Diagnostics { get; set; } = [];
}

public sealed class GridBacktestCycle
{
    public int Id { get; set; }
    public int GridBacktestRunId { get; set; }
    public int Sequence { get; set; }
    public DateTimeOffset QualificationTimeUtc { get; set; }
    public decimal RangeHigh { get; set; }
    public decimal RangeLow { get; set; }
    public decimal QualificationClose { get; set; }
    public decimal Atr { get; set; }
    public decimal Adx { get; set; }
    public decimal Anchor { get; set; }
    public decimal Spacing { get; set; }
    public decimal LongStop { get; set; }
    public decimal ShortStop { get; set; }
    public decimal EntryEquity { get; set; }
    public decimal TargetRiskPercent { get; set; }
    public decimal TargetRiskAmount { get; set; }
    public decimal CommonLots { get; set; }
    public decimal? LongRisk { get; set; }
    public decimal? ShortRisk { get; set; }
    public decimal? LongMargin { get; set; }
    public decimal? ShortMargin { get; set; }
    public string AllowedDirections { get; set; } = string.Empty;
    public List<GridBacktestPlannedLevel> PlannedLevels { get; set; } = [];
    public List<GridBacktestBasket> Baskets { get; set; } = [];
    public List<GridBacktestTelemetry> Telemetry { get; set; } = [];
}

public sealed class GridBacktestPlannedLevel
{
    public int Id { get; set; }
    public int GridBacktestCycleId { get; set; }
    public int LevelNumber { get; set; }
    public string Direction { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Lots { get; set; }
    public decimal? InitialStopRisk { get; set; }
    public decimal? RequiredMargin { get; set; }
    public bool Allowed { get; set; }
}

public sealed class GridBacktestBasket
{
    public int Id { get; set; }
    public int GridBacktestCycleId { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string ExitReason { get; set; } = string.Empty;
    public DateTimeOffset ExitTimeUtc { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal ExitSpread { get; set; }
    public decimal GrossPnl { get; set; }
    public decimal Commission { get; set; }
    public decimal NetPnl { get; set; }
    public decimal EndingBalance { get; set; }
    public decimal UsedMargin { get; set; }
    public decimal ActualFilledInitialStopRisk { get; set; }
    public decimal? PlannedWorstCasePriceRisk { get; set; }
    public decimal? PlannedWorstCaseMargin { get; set; }
    public List<GridBacktestLeg> Legs { get; set; } = [];
}

public sealed class GridBacktestLeg
{
    public int Id { get; set; }
    public int GridBacktestBasketId { get; set; }
    public int LevelNumber { get; set; }
    public string Direction { get; set; } = string.Empty;
    public DateTimeOffset FillTimeUtc { get; set; }
    public decimal PlannedPrice { get; set; }
    public decimal FillPrice { get; set; }
    public decimal Lots { get; set; }
    public decimal RequiredMargin { get; set; }
    public decimal InitialStopRisk { get; set; }
    public decimal EntryCommission { get; set; }
    public decimal ExitCommission { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal GrossPnl { get; set; }
    public decimal NetPnl { get; set; }
}

public sealed class GridBacktestEvent
{
    public int Id { get; set; }
    public int GridBacktestRunId { get; set; }
    public int Sequence { get; set; }
    public DateTimeOffset Time { get; set; }
    public string Type { get; set; } = string.Empty;
    public int? Level { get; set; }
    public decimal? ExecutablePrice { get; set; }
    public string? Detail { get; set; }
}

public sealed class GridBacktestDiagnostic
{
    public int Id { get; set; }
    public int GridBacktestRunId { get; set; }
    public int Sequence { get; set; }
    public DateTimeOffset? Time { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? DomainCode { get; set; }
    public string? Operation { get; set; }
    public string? BrokerSymbol { get; set; }
    public string? Direction { get; set; }
    public string? Detail { get; set; }
    public decimal? Lots { get; set; }
    public decimal? Entry { get; set; }
    public decimal? Stop { get; set; }
}
