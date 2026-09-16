using EmaBot.Api.Models;

namespace EmaBot.Api.Controllers;

public sealed record GridRunResponse(int Id, string StrategyId, string MarketDataSource, string Symbol, string BrokerSymbol, string Interval, string Status, string AccountCurrency, string HistoricalSpreadModel, string HistoricalChartMode, string TradeMode, string? FailureMessage, DateTimeOffset RequestedStartUtc, DateTimeOffset RequestedEndUtc, DateTimeOffset CreatedAtUtc, DateTimeOffset CompletedAtUtc, DateTimeOffset? ActualStartUtc, DateTimeOffset? ActualEndUtc, decimal StartingBalance, decimal EndingBalance, decimal GridBasketRiskPercent, decimal AdxThreshold, decimal AtrSpacingMultiplier, decimal CommissionPerLotPerSide, decimal ContractSize, decimal VolumeMin, decimal VolumeMax, decimal VolumeStep, decimal PointSize, decimal GrossPnl, decimal TotalCommission, decimal NetPnl, decimal AverageNetPnl, decimal MaxDrawdown, decimal? VolumeLimit, decimal? TickSize, decimal? TickValueProfit, decimal? TickValueLoss, decimal? GrossProfitFactor, decimal? NetProfitFactor, int? StopsLevelPoints, int LevelCount, int CooldownBars, int RangeLookback, int AtrPeriod, int AdxPeriod, int ReportingCandleCount, int WarmupCandleCount, int BasketCount, int WinningBaskets, int LosingBaskets, int BreakEvenBaskets, int LongBaskets, int ShortBaskets, int QualifiedCycles, int RejectedQualificationCount, int AmbiguousFirstSideCount, int NoFillCyclesAtEndOfData, int TradeModeBlockedCount, int RiskBelowMinimumVolumeCount, int RiskCannotBeSafelySizedCount, int RiskCalculationUnavailableCount, int MarginCalculationUnavailableCount, int InsufficientMarginCount, int EconomicsCallCount, long EconomicsElapsedMilliseconds);

public sealed record GridCycleResponse(int Id, int GridBacktestRunId, int Sequence, DateTimeOffset QualificationTimeUtc, decimal RangeHigh, decimal RangeLow, decimal QualificationClose, decimal Atr, decimal Adx, decimal Anchor, decimal Spacing, decimal LongStop, decimal ShortStop, decimal EntryEquity, decimal TargetRiskPercent, decimal TargetRiskAmount, decimal CommonLots, decimal? LongRisk, decimal? ShortRisk, decimal? LongMargin, decimal? ShortMargin, string AllowedDirections);

public sealed record GridPlannedLevelResponse(int Id, int GridBacktestCycleId, int LevelNumber, string Direction, decimal Price, decimal Lots, decimal? InitialStopRisk, decimal? RequiredMargin, bool Allowed);

public sealed record GridBasketResponse(int Id, int GridBacktestCycleId, string Direction, string ExitReason, DateTimeOffset ExitTimeUtc, decimal ExitPrice, decimal ExitSpread, decimal GrossPnl, decimal Commission, decimal NetPnl, decimal EndingBalance, decimal UsedMargin, decimal ActualFilledInitialStopRisk, decimal? PlannedWorstCasePriceRisk, decimal? PlannedWorstCaseMargin);

public sealed record GridLegResponse(int Id, int GridBacktestBasketId, int LevelNumber, string Direction, DateTimeOffset FillTimeUtc, decimal PlannedPrice, decimal FillPrice, decimal Lots, decimal RequiredMargin, decimal InitialStopRisk, decimal EntryCommission, decimal ExitCommission, decimal TotalCommission, decimal GrossPnl, decimal NetPnl);

public sealed record GridEventResponse(int Id, int GridBacktestRunId, int Sequence, DateTimeOffset Time, string Type, int? Level, decimal? ExecutablePrice, string? Detail);

public sealed record GridDiagnosticResponse(int Id, int GridBacktestRunId, int Sequence, DateTimeOffset? Time, string Code, string? DomainCode, string? Operation, string? BrokerSymbol, string? Direction, string? Detail, decimal? Lots, decimal? Entry, decimal? Stop);

public sealed record GridCycleDetailResponse(GridCycleResponse Cycle, IReadOnlyList<GridPlannedLevelResponse> PlannedLevels);
public sealed record GridBasketDetailResponse(GridBasketResponse Basket, IReadOnlyList<GridLegResponse> Legs);
public sealed record GridBacktestDetailResponse(GridRunResponse Run, IReadOnlyList<GridCycleDetailResponse> Cycles, IReadOnlyList<GridBasketDetailResponse> Baskets, IReadOnlyList<GridEventResponse> Events, IReadOnlyList<GridDiagnosticResponse> Diagnostics)
{
    public string StrategyId => Run.StrategyId;
}

public static class GridBacktestResponses
{
    public static GridRunResponse ToResponse(GridBacktestRun x) => new(x.Id, x.StrategyId, x.MarketDataSource, x.Symbol, x.BrokerSymbol, x.Interval, x.Status, x.AccountCurrency, x.HistoricalSpreadModel, x.HistoricalChartMode, x.TradeMode, x.FailureMessage, x.RequestedStartUtc, x.RequestedEndUtc, x.CreatedAtUtc, x.CompletedAtUtc, x.ActualStartUtc, x.ActualEndUtc, x.StartingBalance, x.EndingBalance, x.GridBasketRiskPercent, x.AdxThreshold, x.AtrSpacingMultiplier, x.CommissionPerLotPerSide, x.ContractSize, x.VolumeMin, x.VolumeMax, x.VolumeStep, x.PointSize, x.GrossPnl, x.TotalCommission, x.NetPnl, x.AverageNetPnl, x.MaxDrawdown, x.VolumeLimit, x.TickSize, x.TickValueProfit, x.TickValueLoss, x.GrossProfitFactor, x.NetProfitFactor, x.StopsLevelPoints, x.LevelCount, x.CooldownBars, x.RangeLookback, x.AtrPeriod, x.AdxPeriod, x.ReportingCandleCount, x.WarmupCandleCount, x.BasketCount, x.WinningBaskets, x.LosingBaskets, x.BreakEvenBaskets, x.LongBaskets, x.ShortBaskets, x.QualifiedCycles, x.RejectedQualificationCount, x.AmbiguousFirstSideCount, x.NoFillCyclesAtEndOfData, x.TradeModeBlockedCount, x.RiskBelowMinimumVolumeCount, x.RiskCannotBeSafelySizedCount, x.RiskCalculationUnavailableCount, x.MarginCalculationUnavailableCount, x.InsufficientMarginCount, x.EconomicsCallCount, x.EconomicsElapsedMilliseconds);
    public static GridCycleResponse ToResponse(GridBacktestCycle x) => new(x.Id, x.GridBacktestRunId, x.Sequence, x.QualificationTimeUtc, x.RangeHigh, x.RangeLow, x.QualificationClose, x.Atr, x.Adx, x.Anchor, x.Spacing, x.LongStop, x.ShortStop, x.EntryEquity, x.TargetRiskPercent, x.TargetRiskAmount, x.CommonLots, x.LongRisk, x.ShortRisk, x.LongMargin, x.ShortMargin, x.AllowedDirections);
    public static GridPlannedLevelResponse ToResponse(GridBacktestPlannedLevel x) => new(x.Id, x.GridBacktestCycleId, x.LevelNumber, x.Direction, x.Price, x.Lots, x.InitialStopRisk, x.RequiredMargin, x.Allowed);
    public static GridBasketResponse ToResponse(GridBacktestBasket x) => new(x.Id, x.GridBacktestCycleId, x.Direction, x.ExitReason, x.ExitTimeUtc, x.ExitPrice, x.ExitSpread, x.GrossPnl, x.Commission, x.NetPnl, x.EndingBalance, x.UsedMargin, x.ActualFilledInitialStopRisk, x.PlannedWorstCasePriceRisk, x.PlannedWorstCaseMargin);
    public static GridLegResponse ToResponse(GridBacktestLeg x) => new(x.Id, x.GridBacktestBasketId, x.LevelNumber, x.Direction, x.FillTimeUtc, x.PlannedPrice, x.FillPrice, x.Lots, x.RequiredMargin, x.InitialStopRisk, x.EntryCommission, x.ExitCommission, x.TotalCommission, x.GrossPnl, x.NetPnl);
    public static GridEventResponse ToResponse(GridBacktestEvent x) => new(x.Id, x.GridBacktestRunId, x.Sequence, x.Time, x.Type, x.Level, x.ExecutablePrice, x.Detail);
    public static GridDiagnosticResponse ToResponse(GridBacktestDiagnostic x) => new(x.Id, x.GridBacktestRunId, x.Sequence, x.Time, x.Code, x.DomainCode, x.Operation, x.BrokerSymbol, x.Direction, x.Detail, x.Lots, x.Entry, x.Stop);
    public static GridBacktestDetailResponse ToDetail(GridBacktestRun run) => new(ToResponse(run),
        run.Cycles.OrderBy(c => c.Sequence).Select(c => new GridCycleDetailResponse(ToResponse(c), c.PlannedLevels.OrderBy(l => l.Direction).ThenBy(l => l.LevelNumber).Select(ToResponse).ToArray())).ToArray(),
        run.Cycles.SelectMany(c => c.Baskets).OrderBy(b => b.ExitTimeUtc).Select(b => new GridBasketDetailResponse(ToResponse(b), b.Legs.OrderBy(l => l.LevelNumber).Select(ToResponse).ToArray())).ToArray(),
        run.Events.OrderBy(e => e.Sequence).Select(ToResponse).ToArray(), run.Diagnostics.OrderBy(d => d.Sequence).Select(ToResponse).ToArray());
}
