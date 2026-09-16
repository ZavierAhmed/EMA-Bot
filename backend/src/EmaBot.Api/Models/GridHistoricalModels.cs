using EmaBot.Api.Market;
using EmaBot.Api.Strategy.Grid;

namespace EmaBot.Api.Models;

// Non-persisted, account-currency models. No EMA settings or legacy fee inputs.
public sealed record GridHistoricalBacktestRequest(string Symbol, string Interval, decimal StartingBalance,
    string AccountCurrency, decimal PaperCommissionPerLotPerSide, GridRangeSettings? Settings = null,
    DateTimeOffset? RequestedStartUtc = null, DateTimeOffset? RequestedEndUtc = null);
public enum GridHistoricalExitReason { TakeProfit, EmergencyStop, EndOfData }
public enum GridHistoricalEventType { Qualified, Fill, Exit, AmbiguousFirstSide, CanceledWithoutFills, Rejected }
public sealed record GridHistoricalBasketEvent(DateTimeOffset Time, GridHistoricalEventType Type,
    int? Level = null, decimal? ExecutablePrice = null, string? Detail = null);
public sealed record GridHistoricalPlannedLevel(int Number, GridBasketDirection Direction, decimal Price,
    decimal Lots, decimal? InitialStopRisk, decimal? RequiredMargin, bool Allowed);
public sealed record GridHistoricalCycleSnapshot(GridRangeIndicatorSnapshot Indicators, decimal Anchor,
    decimal Spacing, decimal LongStop, decimal ShortStop, decimal TargetRiskPercent, decimal EntryEquity,
    GridRangeSizing Sizing, IReadOnlyList<GridHistoricalPlannedLevel> PlannedLevels);
public sealed record GridHistoricalLeg(int Level, DateTimeOffset FillTime, decimal FillPrice, decimal Lots,
    decimal RequiredMargin, decimal InitialStopRisk, decimal EntryCommission, decimal ExitCommission, decimal GrossPnl)
{
    public decimal Commission => EntryCommission + ExitCommission;
    public decimal NetPnl => GrossPnl - Commission;
}
public sealed record GridHistoricalBasket(GridHistoricalCycleSnapshot Cycle, GridBasketDirection Direction,
    IReadOnlyList<GridHistoricalLeg> Legs, GridHistoricalExitReason ExitReason, DateTimeOffset ExitTime,
    decimal ExitPrice, decimal ExitSpread, decimal EndingBalance, IReadOnlyList<GridHistoricalBasketEvent> Events)
{
    public decimal GrossPnl => Legs.Sum(l => l.GrossPnl);
    public decimal Commission => Legs.Sum(l => l.Commission);
    public decimal NetPnl => GrossPnl - Commission;
    public decimal UsedMargin => Legs.Sum(l => l.RequiredMargin);
    public decimal ActualFilledInitialStopRisk => Legs.Sum(l => l.InitialStopRisk);
    public decimal? PlannedWorstCasePriceRisk => Direction == GridBasketDirection.Long ? Cycle.Sizing.LongRisk : Cycle.Sizing.ShortRisk;
    public decimal? PlannedWorstCaseMargin => Direction == GridBasketDirection.Long ? Cycle.Sizing.LongMargin : Cycle.Sizing.ShortMargin;
    public decimal ExitBid => Direction == GridBasketDirection.Long ? ExitPrice : ExitPrice - ExitSpread;
    public decimal ExitAsk => Direction == GridBasketDirection.Short ? ExitPrice : ExitPrice + ExitSpread;
}
public sealed record GridHistoricalDiagnostic(DateTimeOffset? Time, string Code, GridCycleDiagnostics? DomainCode = null,
    GridRiskDiagnostic? Economics = null, string? Detail = null);
public sealed record GridHistoricalDiagnostics(int QualifiedCycles, int RejectedQualificationCount,
    int AmbiguousFirstSideCount, int NoFillCyclesAtEndOfData, int TradeModeBlockedCount,
    IReadOnlyList<GridHistoricalDiagnostic> Entries)
{
    private int Count(GridCycleDiagnostics code) => Entries.Count(e => e.DomainCode == code);
    public int RiskBelowMinimumVolumeCount => Count(GridCycleDiagnostics.RiskBelowMinimumVolume);
    public int RiskCannotBeSafelySizedCount => Count(GridCycleDiagnostics.RiskCannotBeSafelySized);
    public int RiskCalculationUnavailableCount => Count(GridCycleDiagnostics.RiskCalculationUnavailable);
    public int MarginCalculationUnavailableCount => Count(GridCycleDiagnostics.MarginCalculationUnavailable);
    public int InsufficientMarginCount => Count(GridCycleDiagnostics.InsufficientMargin);
}
public sealed record GridHistoricalBacktestResult(GridHistoricalBacktestRequest Request, InstrumentCatalogItem Instrument,
    DateTimeOffset? ActualStartUtc, DateTimeOffset? ActualEndUtc, int CandleCount, int WarmupCandleCount,
    decimal EndingBalance, decimal MaxDrawdown, IReadOnlyList<GridHistoricalBasket> Baskets,
    IReadOnlyList<GridHistoricalCycleSnapshot> Cycles, GridHistoricalDiagnostics Diagnostics,
    IReadOnlyList<GridHistoricalBasketEvent> Events, int EconomicsCallCount, long EconomicsElapsedMilliseconds)
{
    public string StrategyId => GridRangeSettings.StrategyId;
    public string Symbol => Request.Symbol;
    public string Interval => Request.Interval;
    public string AccountCurrency => Request.AccountCurrency;
    public DateTimeOffset? RequestedStartUtc => Request.RequestedStartUtc;
    public DateTimeOffset? RequestedEndUtc => Request.RequestedEndUtc;
    public decimal StartingBalance => Request.StartingBalance;
    public int BasketCount => Baskets.Count;
    public int WinningBaskets => Baskets.Count(b => b.NetPnl > 0m);
    public int LosingBaskets => Baskets.Count(b => b.NetPnl < 0m);
    public int BreakEvenBaskets => Baskets.Count(b => b.NetPnl == 0m);
    public int LongBaskets => Baskets.Count(b => b.Direction == GridBasketDirection.Long);
    public int ShortBaskets => BasketCount - LongBaskets;
    public decimal GrossPnl => Baskets.Sum(b => b.GrossPnl);
    public decimal Commission => Baskets.Sum(b => b.Commission);
    public decimal NetPnl => GrossPnl - Commission;
    public decimal AverageNetPnl => BasketCount == 0 ? 0m : NetPnl / BasketCount;
    public decimal? GrossProfitFactor => ProfitFactor(Baskets.Select(b => b.GrossPnl));
    public decimal? NetProfitFactor => ProfitFactor(Baskets.Select(b => b.NetPnl));
    private static decimal? ProfitFactor(IEnumerable<decimal> values)
    {
        decimal gain = 0m, loss = 0m;
        foreach (var value in values) { if (value > 0m) gain += value; else loss -= value; }
        return loss == 0m ? null : gain / loss;
    }
}

// An execution economics failure terminates the run, never returning a successful
// result with a silently dropped open basket. Preserve the cycle and fill context.
public sealed class GridHistoricalExecutionException(GridHistoricalDiagnostic diagnostic,
    GridHistoricalCycleSnapshot? cycle = null, Exception? inner = null) : InvalidOperationException(diagnostic.Detail ?? diagnostic.Code, inner)
{
    public GridHistoricalDiagnostic Diagnostic { get; } = diagnostic;
    public GridHistoricalCycleSnapshot? Cycle { get; } = cycle;
}
